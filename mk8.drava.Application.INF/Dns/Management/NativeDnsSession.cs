using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mk8.Drava.Application.BLL.Dns;
using Mk8.Drava.Application.DAL.Storage;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.INF.Dns.Management;

internal sealed class NativeDnsSession : IDisposable
{
    private readonly NativeDnsManagementSettings _settings;
    private readonly NativeDnsManagementClient _client;
    private readonly IDnsMutationJournal _journal;
    private readonly byte[] _credential;
    private readonly byte[] _origin;
    private readonly DnsJournalScope _scope;

    public NativeDnsSession(NativeDnsManagementSettings settings, string siteDomain, string siteId, bool acme,
        IDnsMutationJournal journal, IReadOnlyList<string> approvedAddresses)
    {
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(approvedAddresses);
        settings.Validate(siteDomain, acme); RegistrationSiteTrust.RequireLabel(siteId);
        settings = settings with { Scopes = settings.Scopes.Select(static scope => scope with { }).ToArray() };
        _settings = settings; _journal = journal; _origin = WireName(settings.Origin);
        _credential = ReadCredential(settings.CredentialPath);
        try
        {
            var scopes = settings.Scopes.Select(static item => item.Owner + "/" + item.Type.ToString(System.Globalization.CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal).ToArray();
            var identity = JsonSerializer.SerializeToUtf8Bytes(new { purpose = acme ? "acme" : "records", siteId, settings.TenantId, settings.ZoneId,
                settings.Origin, settings.SocketPath, settings.CredentialPath, credentialDigest = Convert.ToHexString(SHA256.HashData(_credential)), scopes,
                addresses = approvedAddresses.Order(StringComparer.Ordinal).ToArray() });
            _scope = new DnsJournalScope(acme ? "acme" : "records", Convert.ToHexString(SHA256.HashData(identity)));
            _client = new NativeDnsManagementClient(settings.SocketPath);
        }
        catch { CryptographicOperations.ZeroMemory(_credential); throw; }
    }

    public ValueTask<DnsJournalState> ReadJournalAsync(CancellationToken cancellationToken) => _journal.ReadDnsJournalAsync(_scope, cancellationToken);

    public async ValueTask<ManagementReply> ReadAsync(string owner, ushort type, CancellationToken cancellationToken)
    {
        RequireScope(owner, type);
        var request = Request("read", Guid.NewGuid(), 0) with { Selection = [new RrsetKey(WireName(owner), type)] };
        var reply = await _client.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        var name = WireName(owner);
        foreach (var record in reply.Records)
            if (record.Type != type || !record.Owner.Span.SequenceEqual(name)) throw new InvalidDataException("Native read reply contains an unrequested owner/type.");
        return reply;
    }

    public DnsMutationEntry Intent(string owner, ushort type, uint ttl, ReadOnlyMemory<byte> value, long revision,
        Guid operation, bool remove = false, Guid related = default)
    {
        RequireScope(owner, type);
        var entry = new DnsMutationEntry(operation, owner, type, ttl, Convert.ToBase64String(value.Span), remove, related, revision,
            "prepared", 0, 0, "", ManagementHttpProtocol.OperationPath(_settings.TenantId, _settings.ZoneId, operation, _origin));
        entry.Validate(_scope.Purpose); return entry;
    }

    // The caller serializes its profile lifecycle. This entry point never submits an already-journaled intent.
    public async ValueTask<DnsMutationEntry> PrepareAndSendAsync(DnsMutationEntry intent, CancellationToken cancellationToken)
    {
        RequireScope(intent.Owner, intent.Type); intent.Validate(_scope.Purpose);
        var state = await ReadJournalAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(intent.State, "prepared", StringComparison.Ordinal) || state.Entries.Any(entry => entry.OperationId == intent.OperationId))
            throw new InvalidDataException("Native mutation requires a new durable operation identity; recovery uses status only.");
        await _journal.CommitDnsJournalAsync(_scope, state.Revision, new DnsJournalState(checked(state.Revision + 1), [.. state.Entries, intent]), cancellationToken).ConfigureAwait(false);
        var value = (ReadOnlyMemory<byte>)Convert.FromBase64String(intent.Value);
        var request = Request("patch", intent.OperationId, intent.ExpectedZoneRevision) with
        {
            Changes = [new RrsetChange(WireName(intent.Owner), intent.Type, intent.Ttl, intent.Remove ? [] : [value], intent.Remove ? [value] : [], Replace: false)],
        };
        try
        {
            var result = await _client.ExecuteWithReceiptAsync(request, cancellationToken).ConfigureAwait(false);
            return await AcknowledgeAsync(intent, result.Reply, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Conflict)
        {
            await RememberAsync(intent with { State = exception.StatusCode == System.Net.HttpStatusCode.Conflict ? "conflict" : "rejected" }, cancellationToken).ConfigureAwait(false);
            throw;
        }
        // Lost/invalid responses and cancellation retain prepared identity; no automatic PATCH replay/new UUID.
    }

    public async ValueTask<DnsMutationEntry?> RecoverAsync(DnsMutationEntry entry, CancellationToken cancellationToken)
    {
        RequireScope(entry.Owner, entry.Type); entry.Validate(_scope.Purpose);
        if (entry.State is not ("prepared" or "accepted")) return entry;
        try
        {
            var reply = await _client.ExecuteAsync(Request("status", entry.OperationId, 0), cancellationToken).ConfigureAwait(false);
            return await AcknowledgeAsync(entry, reply, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }

    public ValueTask RememberAsync(DnsMutationEntry entry, CancellationToken cancellationToken) => RememberCoreAsync(entry, cancellationToken);

    private async ValueTask RememberCoreAsync(DnsMutationEntry entry, CancellationToken cancellationToken)
    {
        var state = await ReadJournalAsync(cancellationToken).ConfigureAwait(false);
        var entries = state.Entries.ToArray(); var index = Array.FindIndex(entries, candidate => candidate.OperationId == entry.OperationId);
        if (index < 0) throw new InvalidDataException("Native receipt has no durable prepared intent.");
        entries[index] = entry;
        await _journal.CommitDnsJournalAsync(_scope, state.Revision, new DnsJournalState(checked(state.Revision + 1), entries), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<DnsMutationEntry> AcknowledgeAsync(DnsMutationEntry intent, ManagementReply reply, CancellationToken cancellationToken)
    {
        if (reply.OperationId != intent.OperationId) throw new InvalidDataException("Native operation receipt changes its UUID.");
        var entry = intent with { State = reply.State, Revision = reply.Revision, Serial = reply.Serial, ContentHash = reply.ContentHash };
        await RememberAsync(entry, cancellationToken).ConfigureAwait(false); return entry;
    }

    private ManagementRequest Request(string action, Guid operation, long revision) =>
        new(action, _settings.TenantId, _settings.ZoneId, operation, revision, _origin, [], _credential);

    private void RequireScope(string owner, ushort type)
    {
        foreach (var scope in _settings.Scopes) if (string.Equals(scope.Owner, owner, StringComparison.Ordinal) && scope.Type == type) return;
        throw new InvalidDataException("Native DNS owner/type requires an explicitly provisioned static scope.");
    }

    internal static byte[] WireName(string name)
    {
        var bytes = new List<byte>(256);
        foreach (var label in name.Split('.'))
        {
            if (label.Length is < 1 or > 63 || label.Any(static character => !char.IsAscii(character))) throw new InvalidDataException("Invalid canonical DNS wire name.");
            bytes.Add(checked((byte)label.Length)); bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }
        bytes.Add(0); if (bytes.Count > 255) throw new InvalidDataException("DNS wire name exceeds its bound."); return bytes.ToArray();
    }

    private static byte[] ReadCredential(string path)
    {
        var bytes = PrivateOwnerFile.Read(path, 43, 44);
        try
        {
            if (bytes.Length == 44 && bytes[^1] != (byte)'\n') throw new InvalidDataException("Native capability file must contain its canonical base64url value.");
            return ManagementHttpProtocol.DecodeCredential("Bearer " + Encoding.ASCII.GetString(bytes, 0, 43));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Dispose() { _client.Dispose(); CryptographicOperations.ZeroMemory(_credential); }
}
