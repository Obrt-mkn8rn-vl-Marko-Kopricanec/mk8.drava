using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Acme;
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

    public static LocalSiteCertificateAuthority Open(string privatePath, string expectedFingerprint, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        RegistryNames.RequireFingerprint(expectedFingerprint);
        var issuer = X509CertificateLoader.LoadPkcs12(PrivateCertificateFile.Read(privatePath), password: null, X509KeyStorageFlags.EphemeralKeySet);
        try
        {
            using var key = issuer.GetECDsaPrivateKey();
            if (key is null || !RegistrationProof.IsP256(key) || !string.Equals(issuer.GetCertHashString(HashAlgorithmName.SHA256), expectedFingerprint, StringComparison.Ordinal) ||
                !IsAuthority(issuer) || clock.GetUtcNow().UtcDateTime < issuer.NotBefore.ToUniversalTime() || clock.GetUtcNow().UtcDateTime >= issuer.NotAfter.ToUniversalTime())
                throw new InvalidDataException("Site issuer identity, role, key or lifetime is invalid.");
            return new LocalSiteCertificateAuthority(issuer, clock);
        }
        catch { issuer.Dispose(); throw; }
    }

    // Creation is an explicit one-time initialization command; runtime Open never substitutes a new trust root.
    public static async ValueTask<string> InitializeAsync(string privatePath, string siteId, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clock);
        RegistryNames.RequireLabel(siteId);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=mk8.drava " + siteId, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var issuer = request.CreateSelfSigned(clock.GetUtcNow().AddMinutes(-5), clock.GetUtcNow().AddYears(5));
        await PrivateCertificateFile.WriteNewAsync(privatePath, issuer.Export(X509ContentType.Pkcs12), cancellationToken).ConfigureAwait(false);
        return issuer.GetCertHashString(HashAlgorithmName.SHA256);
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
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        var usages = new OidCollection();
        if (client) usages.Add(new Oid("1.3.6.1.5.5.7.3.2"));
        if (server) usages.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
        request.CertificateExtensions.Add(names.Build());
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] |= 1;
        using var certificate = request.Create(_issuer, now.AddMinutes(-5), until, serial);
        return certificate.CopyWithPrivateKey(key);
    }

    private static bool IsAuthority(X509Certificate2 certificate)
    {
        var authority = false;
        var signing = false;
        var extensions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid?.Value is not { } oid || !extensions.Add(oid)) return false;
            if (extension is X509BasicConstraintsExtension basic) authority |= basic.CertificateAuthority;
            if (extension is X509KeyUsageExtension usage) signing |= (usage.KeyUsages & X509KeyUsageFlags.KeyCertSign) != X509KeyUsageFlags.None;
        }
        return authority && signing;
    }
}
