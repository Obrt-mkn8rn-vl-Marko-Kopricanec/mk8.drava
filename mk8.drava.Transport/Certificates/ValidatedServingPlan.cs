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
    public X509Certificate2 EnrollmentCertificate { get; }
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
            plan.Certificates.Count is < 1 or > 2 || plan.EnrollmentCaDer.Length is < 128 or > 16384)
            throw new InvalidDataException("Gateway plan is invalid or outside supported bounds.");
        if (plan.AcknowledgmentLeaseSeconds != 0 && plan.AcknowledgmentLeaseSeconds is < 5 or > 300 ||
            plan.LeafLifetimeDays != 0 && plan.LeafLifetimeDays is < 2 or > 90)
            throw new InvalidDataException("Gateway plan contains invalid acknowledgment or certificate policy.");
        foreach (var certificate in plan.Certificates)
            if (certificate.Pfx.Length is < 128 or > 65536 || certificate.PfxPassword.Length > 256)
                throw new InvalidDataException("Gateway certificate material exceeds its bound.");
        ValidateListeners(plan, bootstrap);
        _root = X509CertificateLoader.LoadCertificate(plan.EnrollmentCaDer.Span);
        try
        {
            if (!string.Equals(_root.GetCertHashString(HashAlgorithmName.SHA256), bootstrap.EnrollmentRootFingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("Gateway plan enrollment root differs from its bootstrap trust.");
            ServingCertificate = LoadServerCertificate(plan.Certificates[0]);
            ValidateNamesAndLifetime(plan, ServingCertificate);
            EnrollmentCertificate = ReadEnrollmentCertificate(plan, bootstrap);
            _plan = plan.Clone();
        }
        catch { DisposeLeaves(); _root.Dispose(); throw; }
    }

    public bool ValidateClientCertificate(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return ValidateLeaf(certificate, client: true);
    }

    public void Dispose()
    {
        DisposeLeaves();
        _root.Dispose();
    }

    private void DisposeLeaves()
    {
        if (!ReferenceEquals(EnrollmentCertificate, ServingCertificate)) EnrollmentCertificate?.Dispose();
        ServingCertificate?.Dispose();
    }

    private X509Certificate2 LoadServerCertificate(ServingCertificate material)
    {
        var certificate = X509CertificateLoader.LoadPkcs12(material.Pfx.Span, material.PfxPassword, X509KeyStorageFlags.EphemeralKeySet);
        try
        {
            if (!certificate.HasPrivateKey || !ValidateLeaf(certificate, client: false))
                throw new InvalidDataException("Gateway server certificate identity or lifetime is invalid.");
            return certificate;
        }
        catch { certificate.Dispose(); throw; }
    }

    private X509Certificate2 ReadEnrollmentCertificate(PresentationPlan plan, GatewayBootstrap bootstrap)
    {
        // Legacy material can be recovered while Application migrates it to distinct keys at a new generation.
        if (plan.Certificates.Count == 1) return ServingCertificate;
        var material = plan.Certificates[1];
        var domain = plan.Certificates[0].HostNames[0][2..];
        if (!string.Equals(material.CertificateId, "enrollment", StringComparison.Ordinal) || material.HostNames.Count != 2 ||
            !string.Equals(material.HostNames[0], "register." + domain, StringComparison.Ordinal) ||
            !string.Equals(material.HostNames[1], "admin." + domain, StringComparison.Ordinal))
            throw new InvalidDataException("Gateway private listener certificate has an invalid scope.");
        var certificate = LoadServerCertificate(material);
        try
        {
            if (!certificate.MatchesHostname(material.HostNames[0], allowWildcards: false, allowCommonName: false) ||
                !certificate.MatchesHostname(material.HostNames[1], allowWildcards: false, allowCommonName: false) ||
                certificate.MatchesHostname("probe." + domain, allowWildcards: true, allowCommonName: false) ||
                material.NotAfterUnixSeconds != new DateTimeOffset(certificate.NotAfter.ToUniversalTime()).ToUnixTimeSeconds() ||
                plan.ValidUntilUnixSeconds > material.NotAfterUnixSeconds ||
                certificate.PublicKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(ServingCertificate.PublicKey.ExportSubjectPublicKeyInfo()))
                throw new InvalidDataException("Gateway private listener names, expiry or key separation are invalid.");
            EnrollmentCertificateScope.Require(certificate, material.HostNames, bootstrap.BindAddress);
            return certificate;
        }
        catch { certificate.Dispose(); throw; }
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
