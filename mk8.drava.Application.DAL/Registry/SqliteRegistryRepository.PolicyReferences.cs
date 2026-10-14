using Mk8.Drava.Application.BLL.NoConf;
using Microsoft.Data.Sqlite;

namespace Mk8.Drava.Application.DAL.Registry;

public sealed partial class SqliteRegistryRepository
{
    // Controlled failure/cancellation seam; production leaves this unset.
    internal Func<PolicyCommitPhase, ValueTask>? PolicyCommitObserver { get; set; }

    private async ValueTask<IReadOnlyList<PolicyRevision>> ReadReferencePoliciesAsync(bool includeHistory, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequirePolicyStorageHealthy();
            var inventory = await ReadReferenceInventoryAsync(transaction: null, cancellationToken).ConfigureAwait(false);
            return includeHistory || inventory.History.Count == 0 ? inventory.History : Array.AsReadOnly<PolicyRevision>([inventory.History[0]]);
        }
        finally { _gate.Release(); }
    }

    private ValueTask<PolicyReferenceInventory> ReadReferenceInventoryAsync(SqliteTransaction? transaction, CancellationToken cancellationToken) =>
        ReadReferenceInventoryAsync(transaction, requireCleanupComplete: true, cancellationToken);

    private async ValueTask<PolicyReferenceInventory> ReadReferenceInventoryAsync(SqliteTransaction? transaction, bool requireCleanupComplete, CancellationToken cancellationToken)
    {
        var payloads = await RequirePayloadStore().ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(payloads.SiteId, _siteId, StringComparison.Ordinal) || payloads.SchemaVersion != 1)
            throw new InvalidDataException("Protected policy archive belongs to another site or schema.");
        var command = _connection.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(false);
        command.Transaction = transaction;
        command.CommandText = """
            SELECT r.revision,p.revision,h.revision,h.payload_digest,h.payload_bytes,h.at_utc,h.actor,h.source
            FROM registry_state r JOIN policy_state p ON p.id=1
            LEFT JOIN policy_revisions h ON h.revision <= p.revision
            WHERE r.id=1 AND r.fence=$fence AND r.site_id=$site AND r.schema_version=1
              AND p.schema_version=2 AND ($requireCleanup=0 OR p.cleanup_complete=1)
            ORDER BY h.revision DESC LIMIT 65
            """;
        command.Parameters.AddWithValue("$fence", _fence);
        command.Parameters.AddWithValue("$site", _siteId);
        command.Parameters.AddWithValue("$requireCleanup", requireCleanupComplete ? 1 : 0);
        long registryRevision;
        long head;
        var history = new List<PolicyRevision>();
        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidDataException("Policy writer fence, schema or cleanup state is invalid.");
            registryRevision = reader.GetInt64(0);
            head = reader.GetInt64(1);
            if (registryRevision < 0 || head < 0) throw new InvalidDataException("Policy authority revision is invalid.");
            if (head != 0)
            {
                do
                {
                    if (await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) || history.Count >= 64 || reader.GetInt64(2) != head - history.Count)
                        throw new InvalidDataException("Policy history is missing, discontinuous or exceeds its bound.");
                    var digest = (byte[])reader.GetValue(3);
                    if (digest.Length != 32) throw new InvalidDataException("Policy reference digest is invalid.");
                    var reference = new PolicyPayloadReference(Convert.ToHexString(digest), reader.GetInt32(4));
                    history.Add(new PolicyRevision(reader.GetInt64(2), payloads.Resolve(reference),
                        DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)), reader.GetString(6), reader.GetString(7)));
                } while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));
            }
            else if (!await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("Uncommitted policy history exists.");
            }
        }
        if (history.Count != Math.Min(head, 64)) throw new InvalidDataException("Policy history is incomplete.");
        command.CommandText = "SELECT COUNT(*) FROM policy_revisions";
        if ((long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? -1L) != history.Count)
            throw new InvalidDataException("Policy history contains unreachable revision metadata.");
        return new PolicyReferenceInventory(registryRevision, head, history.AsReadOnly());
    }

    private async ValueTask<bool> CommitReferencePolicyAsync(long expectedPolicyRevision, long expectedRegistryRevision,
        PolicyRevision replacement, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequirePolicyStorageHealthy();
            var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var transactionLifetime = transaction.ConfigureAwait(false);
            var inventory = await ReadReferenceInventoryAsync(transaction, cancellationToken).ConfigureAwait(false);
            if (inventory.Head != expectedPolicyRevision || inventory.RegistryRevision != expectedRegistryRevision) return false;
            var retained = inventory.History.Select(static revision => PolicyPayloadSnapshot.Describe(revision.CanonicalJson)).ToArray();
            var reference = await RequirePayloadStore().StageAsync(replacement.CanonicalJson, retained, cancellationToken).ConfigureAwait(false);
            if (reference != PolicyPayloadSnapshot.Describe(replacement.CanonicalJson))
                throw new InvalidDataException("Protected policy staging returned an invalid reference.");
            await ObservePolicyCommitAsync(PolicyCommitPhase.ArchiveStaged).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!await WriteReferenceRevisionAsync(transaction, expectedPolicyRevision, expectedRegistryRevision, replacement, reference).ConfigureAwait(false)) return false;
            await ObservePolicyCommitAsync(PolicyCommitPhase.MetadataPrepared).ConfigureAwait(false);
            try
            {
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                await ObservePolicyCommitAsync(PolicyCommitPhase.Committed).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _policyStorageUnhealthy = true;
                await transaction.DisposeAsync().ConfigureAwait(false);
                if (await ReconcilePolicyCommitAsync(replacement).ConfigureAwait(false))
                {
                    _policyStorageUnhealthy = false;
                    return true;
                }
                throw new IOException("Policy commit failed or has an unresolved acceptance state; reopen storage before another mutation.", exception);
            }
            return true;
        }
        finally { _gate.Release(); }
    }

    private ValueTask ObservePolicyCommitAsync(PolicyCommitPhase phase) =>
        PolicyCommitObserver is { } observer ? observer(phase) : ValueTask.CompletedTask;

    private async Task<bool> WriteReferenceRevisionAsync(SqliteTransaction transaction, long previous, long registryRevision,
        PolicyRevision replacement, PolicyPayloadReference reference)
    {
        var update = _connection.CreateCommand();
        await using var updateLifetime = update.ConfigureAwait(false);
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE policy_state SET revision=$next
            WHERE id=1 AND schema_version=2 AND cleanup_complete=1 AND revision=$previous
              AND EXISTS(SELECT 1 FROM registry_state WHERE id=1 AND schema_version=1
                         AND site_id=$site AND fence=$fence AND revision=$registry)
            """;
        update.Parameters.AddWithValue("$next", replacement.Revision);
        update.Parameters.AddWithValue("$previous", previous);
        update.Parameters.AddWithValue("$site", _siteId);
        update.Parameters.AddWithValue("$fence", _fence);
        update.Parameters.AddWithValue("$registry", registryRevision);
        if (await update.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false) != 1) return false;
        var insert = _connection.CreateCommand();
        await using var insertLifetime = insert.ConfigureAwait(false);
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO policy_revisions(revision,payload_digest,payload_bytes,at_utc,actor,source)
            VALUES($revision,$digest,$bytes,$at,$actor,$source);
            DELETE FROM policy_revisions WHERE revision <= $revision - 64;
            """;
        insert.Parameters.AddWithValue("$revision", replacement.Revision);
        insert.Parameters.AddWithValue("$digest", Convert.FromHexString(reference.Digest));
        insert.Parameters.AddWithValue("$bytes", reference.Utf8Bytes);
        insert.Parameters.AddWithValue("$at", replacement.AcceptedAtUtc.ToUnixTimeMilliseconds());
        insert.Parameters.AddWithValue("$actor", replacement.Actor);
        insert.Parameters.AddWithValue("$source", replacement.Source);
        await insert.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ReconcilePolicyCommitAsync(PolicyRevision expected)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var actual = await ReadReferenceInventoryAsync(transaction: null, deadline.Token).ConfigureAwait(false);
            if (actual.Head != expected.Revision || actual.History.Count == 0) return false;
            var head = actual.History[0];
            return string.Equals(head.Digest, expected.Digest, StringComparison.Ordinal)
                && string.Equals(head.CanonicalJson, expected.CanonicalJson, StringComparison.Ordinal)
                && string.Equals(head.Actor, expected.Actor, StringComparison.Ordinal)
                && string.Equals(head.Source, expected.Source, StringComparison.Ordinal)
                && head.AcceptedAtUtc.ToUnixTimeMilliseconds() == expected.AcceptedAtUtc.ToUnixTimeMilliseconds();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or InvalidOperationException or SqliteException or OperationCanceledException)
        {
            return false;
        }
    }

    private IPolicyPayloadStore RequirePayloadStore() => _policyPayloads ?? throw new InvalidDataException("Reference policy storage requires its protected archive.");

    private void RequirePolicyStorageHealthy()
    {
        if (_policyStorageUnhealthy) throw new InvalidOperationException("Policy acceptance is unresolved; reopen durable storage before another operation.");
    }

    private sealed record PolicyReferenceInventory(long RegistryRevision, long Head, IReadOnlyList<PolicyRevision> History);
}
