using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Mk8.Drava.UnitTests;

internal sealed class DevelopmentUpstreamTrustCertificates : IDisposable
{
    private DevelopmentUpstreamTrustCertificates(X509Certificate2 root, X509Certificate2 leaf)
    {
        Root = root;
        Leaf = leaf;
    }

    public X509Certificate2 Root { get; }
    public X509Certificate2 Leaf { get; }

    public static DevelopmentUpstreamTrustCertificates Create(bool expired = false, bool clientOnly = false)
    {
        var now = DateTimeOffset.UtcNow;
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=development-upstream-root", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        using var issuer = rootRequest.CreateSelfSigned(now.AddDays(-3), now.AddDays(3));
        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = new CertificateRequest("CN=backend.drava.invalid", leafKey, HashAlgorithmName.SHA256);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
        {
            new Oid(clientOnly ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1"),
        }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("backend.drava.invalid");
        leafRequest.CertificateExtensions.Add(names.Build());
        using var publicLeaf = leafRequest.Create(issuer, expired ? now.AddDays(-2) : now.AddMinutes(-1),
            expired ? now.AddDays(-1) : now.AddDays(1), RandomNumberGenerator.GetBytes(16));
        var root = X509CertificateLoader.LoadCertificate(issuer.RawData);
        try { return new DevelopmentUpstreamTrustCertificates(root, publicLeaf.CopyWithPrivateKey(leafKey)); }
        catch { root.Dispose(); throw; }
    }

    public void Dispose()
    {
        Leaf.Dispose();
        Root.Dispose();
    }
}
