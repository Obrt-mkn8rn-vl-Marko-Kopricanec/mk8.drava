using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.Dns;

namespace Mk8.Drava.Application.DAL.Registry;

public sealed partial class SqliteRegistryRepository : IDnsMutationJournal
{
    private const int MaximumDnsJournalBytes = 2 * 1024 * 1024;
    private bool _dnsJournalInitialized;

    public async ValueTask<DnsJournalState> ReadDnsJournalAsync(DnsJournalScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope); scope.Validate();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureDnsJournalAsync(cancellationToken).ConfigureAwait(false);
            return await ReadDnsJournalCoreAsync(scope, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask CommitDnsJournalAsync(DnsJournalScope scope, long expectedRevision, DnsJournalState replacement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(replacement); scope.Validate();
        if (expectedRevision < 0 || replacement.Revision != checked(expectedRevision + 1)) throw new InvalidDataException("DNS journal revision must advance once.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(replacement, Json);
        var snapshot = DecodeDnsState(bytes, scope.Purpose);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureDnsJournalAsync(cancellationToken).ConfigureAwait(false);
            var previous = await ReadDnsJournalCoreAsync(scope, cancellationToken).ConfigureAwait(false);
            if (previous.Revision != expectedRevision) throw new InvalidOperationException("DNS journal revision conflict.");
            RequireDnsTransition(previous, snapshot);
            await WriteDnsJournalCoreAsync(scope, previous.Revision, snapshot, bytes, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async ValueTask EnsureDnsJournalAsync(CancellationToken cancellationToken)
    {
        await RequireWriterFenceAsync(cancellationToken).ConfigureAwait(false);
        if (_dnsJournalInitialized) return;
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS dns_mutation_journal(purpose TEXT PRIMARY KEY CHECK(purpose IN ('records','acme')),
                schema_version INTEGER NOT NULL,site_id TEXT NOT NULL,scope TEXT NOT NULL,revision INTEGER NOT NULL,state BLOB NOT NULL,digest BLOB NOT NULL);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _dnsJournalInitialized = true;
    }

    private async ValueTask<DnsJournalState> ReadDnsJournalCoreAsync(DnsJournalScope scope, CancellationToken cancellationToken)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT schema_version,site_id,scope,revision,state,digest FROM dns_mutation_journal WHERE purpose=$purpose";
        command.Parameters.AddWithValue("$purpose", scope.Purpose);
        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using var lifetime = reader.ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return DnsJournalState.Empty;
        if (reader.GetInt32(0) != 1 || !string.Equals(reader.GetString(1), _siteId, StringComparison.Ordinal) || !string.Equals(reader.GetString(2), scope.Fingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("DNS journal belongs to another immutable capability/zone/profile scope.");
        var bytes = (byte[])reader.GetValue(4); var digest = (byte[])reader.GetValue(5);
        if (bytes.Length is < 1 or > MaximumDnsJournalBytes || digest.Length != 32 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), digest))
            throw new InvalidDataException("DNS journal integrity failed.");
        var state = DecodeDnsState(bytes, scope.Purpose);
        if (state.Revision != reader.GetInt64(3)) throw new InvalidDataException("DNS journal revision disagrees with its durable boundary.");
        return state;
    }

    private async ValueTask WriteDnsJournalCoreAsync(DnsJournalScope scope, long previous, DnsJournalState state, byte[] bytes, CancellationToken cancellationToken)
    {
        var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        using var command = _connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dns_mutation_journal(purpose,schema_version,site_id,scope,revision,state,digest)
            SELECT $purpose,1,$site,$scope,$next,$state,$digest FROM registry_state WHERE id=1 AND fence=$fence AND schema_version=1 AND site_id=$site
            ON CONFLICT(purpose) DO UPDATE SET revision=excluded.revision,state=excluded.state,digest=excluded.digest
            WHERE dns_mutation_journal.schema_version=1 AND dns_mutation_journal.site_id=excluded.site_id
                AND dns_mutation_journal.scope=excluded.scope AND dns_mutation_journal.revision=$previous
            """;
        command.Parameters.AddWithValue("$purpose", scope.Purpose); command.Parameters.AddWithValue("$site", _siteId);
        command.Parameters.AddWithValue("$scope", scope.Fingerprint); command.Parameters.AddWithValue("$next", state.Revision);
        command.Parameters.AddWithValue("$state", bytes); command.Parameters.AddWithValue("$digest", SHA256.HashData(bytes));
        command.Parameters.AddWithValue("$fence", _fence); command.Parameters.AddWithValue("$previous", previous);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidDataException("DNS journal scope, revision or writer fence was lost.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DnsJournalState DecodeDnsState(byte[] bytes, string purpose)
    {
        if (bytes.Length is < 1 or > MaximumDnsJournalBytes) throw new InvalidDataException("DNS journal exceeds its bounded capacity.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        RequireDnsProperties(document.RootElement, ["revision", "entries"]);
        var entries = document.RootElement.GetProperty("entries");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 2048) throw new InvalidDataException("DNS journal operation capacity exceeded.");
        foreach (var entry in entries.EnumerateArray())
            RequireDnsProperties(entry, ["operationId", "owner", "type", "ttl", "value", "remove", "relatedOperationId", "expectedZoneRevision", "state", "revision", "serial", "contentHash", "operationLocation"]);
        var state = JsonSerializer.Deserialize<DnsJournalState>(bytes, Json) ?? throw new InvalidDataException("DNS journal state is absent.");
        if (state.Revision <= 0) throw new InvalidDataException("Persisted DNS journal revision is invalid.");
        var ids = new HashSet<Guid>();
        foreach (var entry in state.Entries)
        {
            if (entry is null || !ids.Add(entry.OperationId)) throw new InvalidDataException("DNS journal contains a missing or duplicate operation.");
            entry.Validate(purpose);
            if (entry.Remove) RequireOwnedDnsRemoval(state.Entries, entry);
            else if (string.Equals(entry.State, "completed", StringComparison.Ordinal) && !state.Entries.Any(candidate => candidate.Remove && candidate.RelatedOperationId == entry.OperationId && candidate.State is "activated" or "completed"))
                throw new InvalidDataException("Completed DNS addition requires its acknowledged owned cleanup operation.");
        }
        return state;
    }

    private static void RequireOwnedDnsRemoval(IReadOnlyList<DnsMutationEntry> entries, DnsMutationEntry removal)
    {
        DnsMutationEntry? addition = null;
        foreach (var entry in entries) if (entry.OperationId == removal.RelatedOperationId) { addition = entry; break; }
        if (addition is null || addition.Remove || addition.State is not ("activated" or "completed") ||
            !string.Equals(addition.Owner, removal.Owner, StringComparison.Ordinal) || addition.Type != removal.Type || !string.Equals(addition.Value, removal.Value, StringComparison.Ordinal))
            throw new InvalidDataException("DNS removal has no acknowledged owned value addition.");
    }

    private static void RequireDnsTransition(DnsJournalState previous, DnsJournalState next)
    {
        if (next.Entries.Count < previous.Entries.Count || next.Entries.Count > previous.Entries.Count + 1) throw new InvalidDataException("DNS journal cannot discard operations or batch unrelated additions.");
        var entries = new Dictionary<Guid, DnsMutationEntry>();
        foreach (var entry in next.Entries) entries.Add(entry.OperationId, entry);
        var originalIds = new HashSet<Guid>();
        foreach (var old in previous.Entries)
        {
            originalIds.Add(old.OperationId);
            if (!entries.TryGetValue(old.OperationId, out var entry) || !old.HasSameIntent(entry)) throw new InvalidDataException("DNS journal cannot rewrite immutable operation identity/intent.");
            var allowed = old.State switch
            {
                "prepared" => entry.State is "prepared" or "accepted" or "activated" or "rejected" or "conflict",
                "accepted" => entry.State is "accepted" or "activated",
                "activated" => entry.State is "activated" or "completed",
                _ => string.Equals(entry.State, old.State, StringComparison.Ordinal),
            };
            if (!allowed || old.Revision != 0 && (entry.Revision != old.Revision || entry.Serial != old.Serial || !string.Equals(entry.ContentHash, old.ContentHash, StringComparison.Ordinal)))
                throw new InvalidDataException("DNS journal receipt regressed or changed identity.");
        }
        foreach (var entry in next.Entries)
            if (!originalIds.Contains(entry.OperationId) && !string.Equals(entry.State, "prepared", StringComparison.Ordinal))
                throw new InvalidDataException("New DNS operation requires a durable prepared intent before its receipt.");
    }

    private static void RequireDnsProperties(JsonElement value, string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("DNS journal requires bounded objects.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name) || !expected.Contains(property.Name, StringComparer.Ordinal)) throw new InvalidDataException("DNS journal properties are duplicated or unknown.");
        if (names.Count != expected.Length) throw new InvalidDataException("DNS journal is missing required properties.");
    }
}
