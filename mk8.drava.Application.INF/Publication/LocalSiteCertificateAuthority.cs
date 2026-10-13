using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Registration;
using Mk8.Drava.Transport.Relay;

namespace Mk8.Drava.Application.INF.Publication;

public sealed class LocalSiteCertificateAuthority : IDisposable
{
    private readonly X509Certificate2 _issuer;
    private readonly TimeProvider _clock;

    private LocalSiteCertificateAuthority(X509Certificate2 issuer, TimeProvider clock)
    {
        _issuer = issuer;
        _clock = clock;
    }

    public X509Certificate2 PublicCertificate => X509CertificateLoader.LoadCertificate(_issuer.RawData);
    public string Fingerprint => _issuer.GetCertHashString(HashAlgorithmName.SHA256);

    public static LocalSiteCertificateAuthority Open(SecretBytes material, string expectedFingerprint, TimeProvider clock) =>
        new(PortableCertificate.LoadAuthority(material, expectedFingerprint, clock), clock);

    public SecretBytes PrivateMaterial => PortableCertificate.ExportPrivate(_issuer);

    // Only explicit initialization creates a root; runtime Open never substitutes an identity.
    public static LocalSiteCertificateAuthority Create(string siteId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        RegistryNames.RequireLabel(siteId);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=mk8.drava " + siteId, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: true, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        using var issuer = request.CreateSelfSigned(clock.GetUtcNow().AddMinutes(-5), clock.GetUtcNow().AddYears(5));
        var material = PortableCertificate.ExportPrivate(issuer);
        return Open(material, issuer.GetCertHashString(HashAlgorithmName.SHA256), clock);
    }

    public X509Certificate2 IssueGateway(string domain, IReadOnlyList<string> addresses) => IssueGateway(domain, addresses, 30);

    public X509Certificate2 IssueGateway(string domain, IReadOnlyList<string> addresses, int lifetimeDays)
    {
        if (lifetimeDays is < 2 or > 90) throw new ArgumentOutOfRangeException(nameof(lifetimeDays));
        ArgumentNullException.ThrowIfNull(addresses);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("*." + domain);
        names.AddDnsName("register." + domain);
        foreach (var address in addresses) names.AddIpAddress(IPAddress.Parse(address));
        return Issue("gateway", names, client: false, server: true, lifetimeDays);
    }

    public X509Certificate2 IssueEnrollmentGateway(string domain, IReadOnlyList<string> addresses, int lifetimeDays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        if (lifetimeDays is < 2 or > 90) throw new ArgumentOutOfRangeException(nameof(lifetimeDays));
        ArgumentNullException.ThrowIfNull(addresses);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("register." + domain);
        names.AddDnsName("admin." + domain);
        foreach (var address in addresses) names.AddIpAddress(IPAddress.Parse(address));
        return Issue("enrollment-gateway", names, client: false, server: true, lifetimeDays);
    }

    public X509Certificate2 IssueNode(string nodeId, IReadOnlyList<string> addresses)
    {
        RegistryNames.RequireLabel(nodeId);
        ArgumentNullException.ThrowIfNull(addresses);
        var names = new SubjectAlternativeNameBuilder();
        foreach (var address in addresses) names.AddIpAddress(IPAddress.Parse(address));
        // A node uses the same enrolled key for its constrained private relay; fingerprint grants remain controller-owned.
        return Issue(nodeId, names, client: true, server: true);
    }

    public X509Certificate2 IssueController(string siteId, string controllerEpoch)
    {
        var names = new SubjectAlternativeNameBuilder();
        names.AddUri(ControllerCertificateRole.Identity(siteId, controllerEpoch));
        return Issue("controller", names, client: true, server: false);
    }

    public void Dispose() => _issuer.Dispose();

    private X509Certificate2 Issue(string subject, SubjectAlternativeNameBuilder names, bool client, bool server, int lifetimeDays = 30)
    {
        var now = _clock.GetUtcNow();
        var until = now.AddDays(lifetimeDays);
        var issuerUntil = new DateTimeOffset(_issuer.NotAfter.ToUniversalTime()).AddMinutes(-5);
        if (until > issuerUntil) until = issuerUntil;
        if (until < now.AddDays(1)) throw new InvalidOperationException("Site CA requires renewal before issuing another leaf.");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        var usages = new OidCollection();
        if (client) usages.Add(new Oid("1.3.6.1.5.5.7.3.2"));
        if (server) usages.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, critical: true));
        request.CertificateExtensions.Add(names.Build());
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] |= 1;
        using var certificate = request.Create(_issuer, now.AddMinutes(-5), until, serial);
        return certificate.CopyWithPrivateKey(key);
    }

}
