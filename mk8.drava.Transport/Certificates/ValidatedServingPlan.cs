using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Transport.Certificates;

public sealed class ValidatedServingPlan : IDisposable
{
    private readonly X509Certificate2 _root;
    public X509Certificate2 ServingCertificate { get; }
    private readonly PresentationPlan _plan;
    private readonly TimeProvider _clock;
    private readonly bool _requireCurrent;
    public PresentationPlan Plan => _plan.Clone();

    public ValidatedServingPlan(PresentationPlan plan, GatewayBootstrap bootstrap, TimeProvider clock, bool requireCurrent)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock; _requireCurrent = requireCurrent;
        if (plan.Version != 1 || plan.Generation is < 1 or > long.MaxValue || !PresentationPlanDigest.Verify(plan) || (requireCurrent && plan.ValidUntilUnixSeconds <= clock.GetUtcNow().ToUnixTimeSeconds()) || plan.CalculateSize() > 128 * 1024 ||
            !string.Equals(plan.SiteId, bootstrap.SiteId, StringComparison.Ordinal) || !string.Equals(plan.GatewayId, bootstrap.GatewayId, StringComparison.Ordinal) ||
            plan.Certificates.Count != 1 || plan.EnrollmentCaDer.Length is < 128 or > 16384 || plan.Certificates[0].Pfx.Length is < 128 or > 65536)
            throw new InvalidDataException("Gateway plan is invalid or outside supported bounds.");
        ValidateListeners(plan, bootstrap);
        _root = X509CertificateLoader.LoadCertificate(plan.EnrollmentCaDer.Span);
        try
        {
            if (!string.Equals(_root.GetCertHashString(HashAlgorithmName.SHA256), bootstrap.EnrollmentRootFingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("Gateway plan enrollment root differs from its bootstrap trust.");
            ServingCertificate = X509CertificateLoader.LoadPkcs12(plan.Certificates[0].Pfx.Span, plan.Certificates[0].PfxPassword, X509KeyStorageFlags.EphemeralKeySet);
            if (!ServingCertificate.HasPrivateKey || !ValidateLeaf(ServingCertificate, client: false))
                throw new InvalidDataException("Gateway serving certificate identity or lifetime is invalid.");
            ValidateNamesAndLifetime(plan, ServingCertificate);
            _plan = plan.Clone();
        }
        catch { ServingCertificate?.Dispose(); _root.Dispose(); throw; }
    }

    public bool ValidateClientCertificate(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return ValidateLeaf(certificate, client: true);
    }

    public void Dispose()
    {
        ServingCertificate.Dispose();
        _root.Dispose();
    }

    private bool ValidateLeaf(X509Certificate2 certificate, bool client)
    {
        var leaf = false;
        var requiredUsage = false;
        var digitalSignature = false;
        var extensions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid?.Value is not { } oid || !extensions.Add(oid)) return false;
            if (extension is X509BasicConstraintsExtension constraints) leaf = !constraints.CertificateAuthority;
            if (extension is X509EnhancedKeyUsageExtension usages)
                foreach (Oid usage in usages.EnhancedKeyUsages)
                    if (string.Equals(usage.Value, client ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1", StringComparison.Ordinal)) requiredUsage = true;
            if (extension is X509KeyUsageExtension key) digitalSignature = (key.KeyUsages & X509KeyUsageFlags.DigitalSignature) != X509KeyUsageFlags.None;
        }
        if (!leaf || !requiredUsage || !digitalSignature) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.VerificationTime = (_requireCurrent || client ? _clock.GetUtcNow() : new DateTimeOffset(certificate.NotBefore.ToUniversalTime()).AddSeconds(1)).UtcDateTime;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(_root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(client ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1"));
        return chain.Build(certificate);
    }

    private static void ValidateNamesAndLifetime(PresentationPlan plan, X509Certificate2 certificate)
    {
        var serving = plan.Certificates[0];
        if (!string.Equals(serving.CertificateId, "site", StringComparison.Ordinal) || serving.HostNames.Count != 2 ||
            !serving.HostNames[0].StartsWith("*.", StringComparison.Ordinal) ||
            !string.Equals(serving.HostNames[1], "register." + serving.HostNames[0][2..], StringComparison.Ordinal) ||
            !certificate.MatchesHostname("probe." + serving.HostNames[0][2..], allowWildcards: true, allowCommonName: false) ||
            !certificate.MatchesHostname(serving.HostNames[1], allowWildcards: true, allowCommonName: false) ||
            serving.NotAfterUnixSeconds != new DateTimeOffset(certificate.NotAfter.ToUniversalTime()).ToUnixTimeSeconds() ||
            plan.ValidUntilUnixSeconds > serving.NotAfterUnixSeconds)
            throw new InvalidDataException("Gateway serving names or expiry differ from its certificate.");
    }

    private static void ValidateListeners(PresentationPlan plan, GatewayBootstrap bootstrap)
    {
        var expected = (bootstrap.HttpPort > 0 ? 1 : 0) + (bootstrap.HttpsPort > 0 ? 1 : 0) + (bootstrap.ManagementPort > 0 ? 1 : 0) + 1;
        if (plan.Listeners.Count != expected) throw new InvalidDataException("Gateway plan changes its bootstrap listener scope.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var listener in plan.Listeners)
        {
            if (!ids.Add(listener.Id) || !IPAddress.TryParse(listener.Address, out var address) || !address.Equals(IPAddress.Parse(bootstrap.BindAddress)))
                throw new InvalidDataException("Gateway plan has an invalid listener identity or address.");
            var allowed = listener.Id switch
            {
                "http" => bootstrap.HttpPort > 0 && listener.Port == bootstrap.HttpPort && !listener.Tls && !listener.Registration,
                "https" => bootstrap.HttpsPort > 0 && listener.Port == bootstrap.HttpsPort && listener.Tls && !listener.Registration,
                "registration" => listener.Port == bootstrap.RegistrationPort && listener.Tls && listener.Registration,
                "management" => bootstrap.ManagementPort > 0 && listener.Port == bootstrap.ManagementPort && listener.Tls && !listener.Registration,
                _ => false,
            };
            if (!allowed) throw new InvalidDataException("Gateway plan requests an unapproved listener.");
            if (listener.Protocols.Count != (listener.Tls ? 2 : 1) || !string.Equals(listener.Protocols[0], "http1", StringComparison.Ordinal) ||
                (listener.Tls && !string.Equals(listener.Protocols[1], "http2", StringComparison.Ordinal)))
                throw new InvalidDataException("Gateway plan requests an unsupported protocol profile.");
        }
    }
}
