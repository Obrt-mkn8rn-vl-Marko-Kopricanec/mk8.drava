using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentPublicServingCertificate : IDisposable
{
    public X509Certificate2 Root { get; }
    public X509Certificate2 Leaf { get; }
    public X509Certificate2 Client { get; }
    public byte[] Pfx { get; }

    private DevelopmentPublicServingCertificate(X509Certificate2 root, X509Certificate2 leaf, X509Certificate2 client, byte[] pfx)
    { Root = root; Leaf = leaf; Client = client; Pfx = pfx; }

    public static DevelopmentPublicServingCertificate Create(DateTimeOffset now, bool includeClientUsage = false)
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = AuthorityRequest("CN=development-public-root", rootKey, pathLength: 1);
        using var issuer = rootRequest.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(1));
        using var intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var intermediateRequest = AuthorityRequest("CN=development-public-intermediate", intermediateKey, pathLength: 0);
        using var intermediatePublic = intermediateRequest.Create(issuer, now.AddMinutes(-4), now.AddMonths(6), RandomNumberGenerator.GetBytes(16));
        using var intermediate = intermediatePublic.CopyWithPrivateKey(intermediateKey);
        X509Certificate2? root = null;
        X509Certificate2? leaf = null;
        X509Certificate2? client = null;
        try
        {
            root = X509CertificateLoader.LoadCertificate(issuer.RawData);
            leaf = IssueLeaf(intermediate, now, client: false, includeClientUsage);
            client = IssueLeaf(intermediate, now, client: true);
            using var publicIntermediate = X509CertificateLoader.LoadCertificate(intermediate.RawData);
            var chain = new X509Certificate2Collection { leaf, publicIntermediate, root };
            var pfx = chain.Export(X509ContentType.Pkcs12) ?? throw new InvalidOperationException("Development chain export failed.");
            return new DevelopmentPublicServingCertificate(root, leaf, client, pfx);
        }
        catch { client?.Dispose(); leaf?.Dispose(); root?.Dispose(); throw; }
    }

    public void Dispose() { Client.Dispose(); Leaf.Dispose(); Root.Dispose(); }

    private static CertificateRequest AuthorityRequest(string name, ECDsa key, int pathLength)
    {
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, pathLength, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request;
    }

    private static X509Certificate2 IssueLeaf(X509Certificate2 issuer, DateTimeOffset now, bool client, bool includeClientUsage = false)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=development-public-leaf", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        var usages = new OidCollection { new Oid(client ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1") };
        if (!client && includeClientUsage) usages.Add(new Oid("1.3.6.1.5.5.7.3.2"));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("*.site.test"); names.AddDnsName("register.site.test"); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var leaf = request.Create(issuer, now.AddMinutes(-3), now.AddDays(30), RandomNumberGenerator.GetBytes(16));
        return leaf.CopyWithPrivateKey(key);
    }
}
