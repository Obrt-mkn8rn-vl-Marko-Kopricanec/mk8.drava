using System.Text;
using Mk8.Drava.Application.BLL.Dns;
using Mk8.Drava.Application.INF.Acme;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.INF.Dns.Management;

internal sealed class NativeAcmeDns01ChallengeProvider : IAcmeDns01ChallengeProvider, IDisposable
{
    private readonly NativeDnsSession _session;
    private readonly NativeDnsServingVerifier _serving;
    private readonly uint _ttl;
    private readonly Guid _instance = Guid.NewGuid();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<Guid> _active = [];
    private bool _recovered;

    public NativeAcmeDns01ChallengeProvider(NativeDnsManagementSettings settings, string siteDomain, string siteId,
        int ttl, string authority, int port, IDnsMutationJournal journal)
    {
        if (ttl is < 60 or > 3600) throw new InvalidDataException("Native DNS01 requires its bounded challenge TTL.");
        _ttl = checked((uint)ttl);
        _serving = new NativeDnsServingVerifier(authority, port, settings.Origin);
        _session = new NativeDnsSession(settings, siteDomain, siteId, acme: true, journal, []);
    }

    public async ValueTask<AcmeDns01Record> PublishAsync(string host, string value, string operationId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(operationId, "N", out var operation) || !string.Equals(operationId, operation.ToString("N"), StringComparison.Ordinal))
            throw new InvalidDataException("Native DNS01 requires a canonical challenge operation UUID.");
        var data = Txt(value);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_recovered) await RecoverCoreAsync(cancellationToken).ConfigureAwait(false);
            if (_active.Count >= 8 || _active.Contains(operation)) throw new InvalidDataException("DNS01 challenge admission is full or duplicated.");
            var reply = await _session.ReadAsync(host, 16, cancellationToken).ConfigureAwait(false);
            if (reply.Records.Any(record => record.Data.Span.SequenceEqual(data)))
                throw new InvalidDataException("Native DNS01 cannot claim a preexisting identical foreign challenge value.");
            var ttl = reply.Records.Count == 0 ? _ttl : reply.Records[0].Ttl;
            if (ttl is < 60 or > 3600 || reply.Records.Any(record => record.Ttl != ttl)) throw new InvalidDataException("Existing DNS01 RRset TTL exceeds the accepted challenge profile.");
            _ = await _session.PrepareAndSendAsync(_session.Intent(host, 16, ttl, data, reply.Revision, operation), cancellationToken).ConfigureAwait(false);
            _active.Add(operation);
            return new PublishedRecord(host, value, operation, _instance);
        }
        catch { _recovered = false; throw; }
        finally { _gate.Release(); }
    }

    public async ValueTask<bool> IsPropagatedAsync(AcmeDns01Record record, CancellationToken cancellationToken)
    {
        var identity = RequireOwned(record);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_active.Contains(identity.Operation)) throw new InvalidDataException("Native challenge is no longer active.");
            var state = await _session.ReadJournalAsync(cancellationToken).ConfigureAwait(false);
            var entry = state.Entries.First(candidate => candidate.OperationId == identity.Operation);
            var current = await _session.RecoverAsync(entry, cancellationToken).ConfigureAwait(false);
            return current is not null && string.Equals(current.State, "activated", StringComparison.Ordinal) &&
                await _serving.VerifyAsync(identity.Host, 16, [Txt(identity.Value)], current.Serial, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask RemoveAsync(AcmeDns01Record record, CancellationToken cancellationToken)
    {
        var identity = RequireOwned(record);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_active.Contains(identity.Operation)) throw new InvalidDataException("Native challenge is no longer active.");
            var state = await _session.ReadJournalAsync(cancellationToken).ConfigureAwait(false);
            var entry = state.Entries.First(candidate => candidate.OperationId == identity.Operation);
            await CleanupAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch { _recovered = false; throw; }
        finally { _active.Remove(identity.Operation); _gate.Release(); }
    }

    public async ValueTask RecoverAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await RecoverCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async ValueTask RecoverCoreAsync(CancellationToken cancellationToken)
    {
        _recovered = false;
        if (_active.Count != 0) throw new InvalidDataException("Native DNS01 recovery requires joined active challenges.");
        var state = await _session.ReadJournalAsync(cancellationToken).ConfigureAwait(false);
        foreach (var addition in state.Entries)
            if (!addition.Remove && addition.State is not ("completed" or "rejected" or "conflict"))
                await CleanupAsync(addition, cancellationToken).ConfigureAwait(false);
        _recovered = true;
    }

    private async ValueTask CleanupAsync(DnsMutationEntry addition, CancellationToken cancellationToken)
    {
        var current = await _session.RecoverAsync(addition, cancellationToken).ConfigureAwait(false);
        if (current is null || !string.Equals(current.State, "activated", StringComparison.Ordinal)) throw new InvalidDataException("Native DNS01 creation remains unresolved; its identity is retained.");
        var state = await _session.ReadJournalAsync(cancellationToken).ConfigureAwait(false);
        if (state.Entries.Any(entry => entry.Remove && entry.RelatedOperationId == current.OperationId && string.Equals(entry.State, "rejected", StringComparison.Ordinal)))
            throw new InvalidDataException("Native DNS01 cleanup was definitively rejected and requires operator reconciliation.");
        var removal = state.Entries.LastOrDefault(entry => entry.Remove && entry.RelatedOperationId == current.OperationId && entry.State is not ("conflict" or "rejected"));
        if (removal is not null)
        {
            removal = await _session.RecoverAsync(removal, cancellationToken).ConfigureAwait(false);
            if (removal is null || removal.State is not ("activated" or "completed"))
                throw new InvalidDataException("Native DNS01 cleanup remains unresolved; no mutation replay is permitted.");
        }
        else
        {
            var reply = await _session.ReadAsync(current.Owner, 16, cancellationToken).ConfigureAwait(false);
            RequireContinuousOwnership(current, reply.Revision, state.Entries);
            var data = Convert.FromBase64String(current.Value);
            if (!reply.Records.Any(record => record.Data.Span.SequenceEqual(data)))
                throw new InvalidDataException("Native DNS01 value changed outside its owned operation chain; cleanup requires reconciliation.");
            removal = await _session.PrepareAndSendAsync(_session.Intent(current.Owner, 16, current.Ttl, data, reply.Revision,
                Guid.NewGuid(), remove: true, related: current.OperationId), cancellationToken).ConfigureAwait(false);
            if (!string.Equals(removal.State, "activated", StringComparison.Ordinal)) throw new InvalidDataException("Native DNS01 cleanup is accepted but not activated.");
        }
        if (!await _serving.VerifyAbsentAsync(current.Owner, 16, Convert.FromBase64String(current.Value), removal.Serial, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Native DNS01 cleanup has no authoritative absence proof.");
        if (!string.Equals(removal.State, "completed", StringComparison.Ordinal)) await _session.RememberAsync(removal with { State = "completed" }, cancellationToken).ConfigureAwait(false);
        await _session.RememberAsync(current with { State = "completed" }, cancellationToken).ConfigureAwait(false);
    }

    // READ supplies no per-value provenance. A gap, even an unrelated external edit, refuses deletion.
    private static void RequireContinuousOwnership(DnsMutationEntry addition, long revision, IReadOnlyList<DnsMutationEntry> entries)
    {
        var cursor = addition.Revision;
        foreach (var entry in entries.Where(static entry => entry.Revision > 0).OrderBy(static entry => entry.Revision))
            if (entry.ExpectedZoneRevision == cursor && entry.Revision <= revision) cursor = entry.Revision;
        if (cursor != revision) throw new InvalidDataException("Native DNS01 cannot establish uninterrupted value ownership across external zone revisions.");
    }

    private PublishedRecord RequireOwned(AcmeDns01Record record) => record is PublishedRecord identity && identity.Instance == _instance
        ? identity : throw new InvalidDataException("Native DNS01 cleanup requires an active record created by this provider instance.");
    private static byte[] Txt(string value)
    {
        if (value is null || value.Length != 43 || value.Any(static c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new InvalidDataException("DNS01 requires its canonical43-character SHA256 base64url digest.");
        return [43, .. Encoding.ASCII.GetBytes(value)];
    }
    public void Dispose() { _session.Dispose(); _gate.Dispose(); }
    private sealed record PublishedRecord(string Host, string Value, Guid Operation, Guid Instance) : AcmeDns01Record(Host, Value);
}
