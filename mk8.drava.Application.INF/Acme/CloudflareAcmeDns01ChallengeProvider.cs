using Mk8.Drava.Application.DAL.Administration;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.INF.NoConf;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.INF.Acme;

internal sealed class CloudflareAcmeDns01ChallengeProvider : IAcmeDns01ChallengeProvider, IDisposable
{
    private readonly CloudflareDnsApi _api;
    private readonly DnsPublicationSettings _settings;
    private readonly string _siteDomain;
    private readonly string _siteId;
    private readonly Guid _instance = Guid.NewGuid();
    private readonly IAcmeDns01PropagationVerifier _verifier;
    private readonly IAcmeDns01CleanupJournal? _journal;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _activeOperations = new(StringComparer.Ordinal);
    private bool _recovered;

    public CloudflareAcmeDns01ChallengeProvider(DnsPublicationSettings settings, string siteDomain, string siteId, IAcmeDns01PropagationVerifier verifier, HttpMessageHandler? handler = null, IAcmeDns01CleanupJournal? journal = null)
    {
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(verifier);
        settings.Validate(siteDomain); RegistrationSiteTrust.RequireLabel(siteId);
        if (!string.Equals(settings.Provider, "cloudflare", StringComparison.Ordinal)) throw new InvalidDataException("DNS01 requires explicit owner-approved Cloudflare settings.");
        _settings = settings; _siteDomain = siteDomain; _siteId = siteId; _verifier = verifier;
        _journal = journal;
        _api = new CloudflareDnsApi(settings.ZoneId, PrivateBearerCredentialFile.Read(settings.CredentialPath), handler);
    }

    public async ValueTask<AcmeDns01Record> PublishAsync(string host, string value, string operationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_recovered)
            {
                if (_activeOperations.Count != 0) throw new InvalidDataException("DNS01 recovery requires joined active challenges.");
                await RecoverOwnedAsync(cancellationToken).ConfigureAwait(false);
            }
            if (_activeOperations.Count == 8 || _activeOperations.Contains(operationId)) throw new InvalidDataException("DNS01 operation admission is full or duplicated.");
            var record = await PublishOwnedAsync(host, value, operationId, cancellationToken).ConfigureAwait(false);
            _activeOperations.Add(operationId);
            return record;
        }
        finally { _gate.Release(); }
    }

    private async ValueTask<AcmeDns01Record> PublishOwnedAsync(string host, string value, string operationId, CancellationToken cancellationToken)
    {
        const string prefix = "_acme-challenge.";
        if (!host.StartsWith(prefix, StringComparison.Ordinal) || host.Length > 253 ||
            !Guid.TryParseExact(operationId, "N", out var operation) || !string.Equals(operationId, operation.ToString("N"), StringComparison.Ordinal))
            throw new InvalidDataException("DNS01 challenge exceeds its canonical operation scope.");
        var domain = host[prefix.Length..];
        foreach (var label in domain.Split('.')) RegistrationSiteTrust.RequireLabel(label);
        if (!string.Equals(domain, _siteDomain, StringComparison.Ordinal) && !domain.EndsWith("." + _siteDomain, StringComparison.Ordinal))
            throw new InvalidDataException("DNS01 challenge is outside its enrolled site.");
        using var deadline = Deadline(cancellationToken);
        if (!string.Equals(await _api.ReadZoneNameAsync(deadline.Token).ConfigureAwait(false), _settings.ZoneName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DNS01 provider zone identity does not match its owner-approved name.");
        for (var ancestor = host; !string.Equals(ancestor, _settings.ZoneName, StringComparison.Ordinal); ancestor = ancestor[(ancestor.IndexOf('.', StringComparison.Ordinal) + 1)..])
        {
            using var delegation = await _api.ListAsync(ancestor, deadline.Token, nameServersOnly: true).ConfigureAwait(false);
            if (delegation.RootElement.GetProperty("result").GetArrayLength() != 0) throw new InvalidDataException("DNS01 challenge is delegated outside the approved provider zone.");
        }
        using (var existing = await _api.ListAsync(host, deadline.Token).ConfigureAwait(false))
            foreach (var record in existing.RootElement.GetProperty("result").EnumerateArray())
                if (string.Equals(record.GetProperty("type").GetString(), "CNAME", StringComparison.Ordinal))
                    throw new InvalidDataException("DNS01 challenge uses an unapproved alias.");
        var intent = new AcmeDns01CleanupEntry(_settings.ZoneId, _siteDomain, _siteId, operationId, host, value, "");
        if (_journal is not null) await _journal.PutAsync(intent, deadline.Token).ConfigureAwait(false);
        try
        {
            var identity = await _api.CreateTxtAsync(host, value, _settings.TtlSeconds, Ownership(operationId), deadline.Token).ConfigureAwait(false);
            if (_journal is not null) await _journal.PutAsync(new AcmeDns01CleanupEntry(intent.ZoneId, intent.SiteDomain, intent.SiteId, operationId, host, value, identity.Id), deadline.Token).ConfigureAwait(false);
            return new PublishedRecord(identity, _instance, operationId);
        }
        catch { _recovered = false; throw; }
    }

    public ValueTask<bool> IsPropagatedAsync(AcmeDns01Record record, CancellationToken cancellationToken)
    {
        var identity = RequireOwned(record);
        return _verifier.VerifyAsync(identity.Host, identity.Value, cancellationToken);
    }

    public async ValueTask RemoveAsync(AcmeDns01Record record, CancellationToken cancellationToken)
    {
        var identity = RequireOwned(record);
        var operationId = ((PublishedRecord)record).OperationId;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var deadline = Deadline(cancellationToken);
            if (!await _api.DeleteTxtAsync(identity, deadline.Token).ConfigureAwait(false)) throw new InvalidDataException("DNS01 cleanup did not confirm the exact owned record.");
            if (_journal is not null) await _journal.RemoveAsync(operationId, deadline.Token).ConfigureAwait(false);
        }
        catch { _recovered = false; throw; }
        finally { _activeOperations.Remove(operationId); _gate.Release(); }
    }

    public async ValueTask RecoverAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activeOperations.Count != 0) throw new InvalidDataException("DNS01 recovery requires joined active challenges.");
            await RecoverOwnedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async ValueTask RecoverOwnedAsync(CancellationToken cancellationToken)
    {
        _recovered = false;
        cancellationToken.ThrowIfCancellationRequested();
        if (_journal is null) { _recovered = true; return; }
        var entries = _journal.Read();
        foreach (var entry in entries)
            if (!string.Equals(entry.ZoneId, _settings.ZoneId, StringComparison.Ordinal) || !string.Equals(entry.SiteDomain, _siteDomain, StringComparison.Ordinal) || !string.Equals(entry.SiteId, _siteId, StringComparison.Ordinal))
                throw new InvalidDataException("DNS01 journal belongs to another owner-approved site or zone.");
        foreach (var entry in entries)
        {
            using var deadline = Deadline(cancellationToken);
            if (!string.Equals(await _api.ReadZoneNameAsync(deadline.Token).ConfigureAwait(false), _settings.ZoneName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Recovered DNS01 zone identity does not match its owner-approved name.");
            var identity = await _api.FindRecoveryTxtAsync(entry.Host, entry.Value, Ownership(entry.OperationId), entry.RecordId, deadline.Token).ConfigureAwait(false);
            if (identity is null && entry.RecordId.Length == 0) throw new InvalidDataException("DNS01 creation remains ambiguous; its durable intent is retained.");
            if (identity is not null)
            {
                if (entry.RecordId.Length == 0)
                    await _journal.PutAsync(new AcmeDns01CleanupEntry(entry.ZoneId, entry.SiteDomain, entry.SiteId, entry.OperationId, entry.Host, entry.Value, identity.Id), deadline.Token).ConfigureAwait(false);
                if (!await _api.DeleteTxtAsync(identity, deadline.Token).ConfigureAwait(false)) throw new InvalidDataException("Recovered DNS01 cleanup did not confirm exact ownership.");
            }
            await _journal.RemoveAsync(entry.OperationId, deadline.Token).ConfigureAwait(false);
        }
        _recovered = true;
    }

    private string Ownership(string operationId) => "mk8.drava acme site=" + _siteId + " op=" + operationId;

    private CloudflareTxtRecord RequireOwned(AcmeDns01Record record) =>
        record is PublishedRecord published && published.ProviderInstance == _instance ? published.Identity : throw new InvalidDataException("DNS01 cleanup requires a record created by this provider instance.");
    private CancellationTokenSource Deadline(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));
        return deadline;
    }
    public void Dispose() { _api.Dispose(); _gate.Dispose(); }
    private sealed record PublishedRecord(CloudflareTxtRecord Identity, Guid ProviderInstance, string OperationId) : AcmeDns01Record(Identity.Host, Identity.Value);
}
