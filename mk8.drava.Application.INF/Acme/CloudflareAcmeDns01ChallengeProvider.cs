using Mk8.Drava.Application.DAL.Administration;
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

    public CloudflareAcmeDns01ChallengeProvider(DnsPublicationSettings settings, string siteDomain, string siteId, IAcmeDns01PropagationVerifier verifier, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(verifier);
        settings.Validate(siteDomain); RegistrationSiteTrust.RequireLabel(siteId);
        if (!string.Equals(settings.Provider, "cloudflare", StringComparison.Ordinal)) throw new InvalidDataException("DNS01 requires explicit owner-approved Cloudflare settings.");
        _settings = settings; _siteDomain = siteDomain; _siteId = siteId; _verifier = verifier;
        _api = new CloudflareDnsApi(settings.ZoneId, PrivateBearerCredentialFile.Read(settings.CredentialPath), handler);
    }

    public async ValueTask<AcmeDns01Record> PublishAsync(string host, string value, string operationId, CancellationToken cancellationToken)
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
        var identity = await _api.CreateTxtAsync(host, value, _settings.TtlSeconds, "mk8.drava acme site=" + _siteId + " op=" + operationId, deadline.Token).ConfigureAwait(false);
        return new PublishedRecord(identity, _instance);
    }

    public ValueTask<bool> IsPropagatedAsync(AcmeDns01Record record, CancellationToken cancellationToken)
    {
        var identity = RequireOwned(record);
        return _verifier.VerifyAsync(identity.Host, identity.Value, cancellationToken);
    }

    public async ValueTask RemoveAsync(AcmeDns01Record record, CancellationToken cancellationToken)
    {
        var identity = RequireOwned(record);
        using var deadline = Deadline(cancellationToken);
        if (!await _api.DeleteTxtAsync(identity, deadline.Token).ConfigureAwait(false)) throw new InvalidDataException("DNS01 cleanup did not confirm the exact owned record.");
    }

    private CloudflareTxtRecord RequireOwned(AcmeDns01Record record) =>
        record is PublishedRecord published && published.ProviderInstance == _instance ? published.Identity : throw new InvalidDataException("DNS01 cleanup requires a record created by this provider instance.");
    private CancellationTokenSource Deadline(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));
        return deadline;
    }
    public void Dispose() => _api.Dispose();
    private sealed record PublishedRecord(CloudflareTxtRecord Identity, Guid ProviderInstance) : AcmeDns01Record(Identity.Host, Identity.Value);
}
