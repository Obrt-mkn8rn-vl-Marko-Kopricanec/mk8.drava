using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Transport.Certificates;

internal sealed class ServerCertificateBundle : IDisposable
{
    private readonly X509Certificate2Collection _certificates;
    public X509Certificate2 Certificate { get; }
    public SslStreamCertificateContext Context { get; }

    private ServerCertificateBundle(X509Certificate2Collection certificates, X509Certificate2 certificate)
    {
        _certificates = certificates; Certificate = certificate;
        Context = SslStreamCertificateContext.Create(certificate, certificates, offline: true);
    }

    public static ServerCertificateBundle Load(ServingCertificate material, ServingTrustSettings trust, X509Certificate2 privateRoot, TimeProvider clock, bool requireCurrent)
    {
        var certificates = X509CertificateLoader.LoadPkcs12Collection(material.Pfx.Span, material.PfxPassword,
            X509KeyStorageFlags.EphemeralKeySet, new Pkcs12LoaderLimits { MaxCertificates = 16, MaxKeys = 1 });
        try
        {
            X509Certificate2? leaf = null;
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var certificate in certificates)
            {
                if (!identities.Add(certificate.GetCertHashString(HashAlgorithmName.SHA256)))
                    throw new InvalidDataException("Server bundle contains duplicate certificates.");
                if (certificate.HasPrivateKey)
                {
                    if (leaf is not null) throw new InvalidDataException("Server bundle contains multiple private keys.");
                    leaf = certificate;
                }
                else if (!IsAuthority(certificate)) throw new InvalidDataException("Server bundle contains an unrelated leaf certificate.");
            }
            if (leaf is null || !HasServerRole(leaf, requireServerOnly: string.Equals(trust.Mode, "site-ca", StringComparison.Ordinal)))
                throw new InvalidDataException("Server bundle requires one non-CA server leaf and its private key.");
            using var chain = new X509Chain();
            chain.ChainPolicy.VerificationTime = (requireCurrent ? clock.GetUtcNow() : new DateTimeOffset(leaf.NotBefore.ToUniversalTime()).AddSeconds(1)).UtcDateTime;
            chain.ChainPolicy.ExtraStore.AddRange(certificates);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            if (!string.Equals(trust.Mode, "system", StringComparison.Ordinal))
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                if (string.Equals(trust.Mode, "site-ca", StringComparison.Ordinal)) chain.ChainPolicy.CustomTrustStore.Add(privateRoot);
                else
                {
                    foreach (var certificate in certificates)
                        if (string.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256), trust.RootFingerprint, StringComparison.Ordinal) && IsAuthority(certificate))
                            chain.ChainPolicy.CustomTrustStore.Add(certificate);
                    if (chain.ChainPolicy.CustomTrustStore.Count != 1) throw new InvalidDataException("Server bundle does not contain its explicitly pinned root.");
                }
            }
            if (!chain.Build(leaf)) throw new InvalidDataException("Server certificate does not satisfy its approved trust or lifetime.");
            // The private issuer can be outside its leaf-only bundle. Supply the verified chain to TLS without downloading issuers.
            foreach (var element in chain.ChainElements)
                if (identities.Add(element.Certificate.GetCertHashString(HashAlgorithmName.SHA256)))
                    certificates.Add(X509CertificateLoader.LoadCertificate(element.Certificate.RawData));
            return new ServerCertificateBundle(certificates, leaf);
        }
        catch { DisposeCertificates(certificates); throw; }
    }

    public void Dispose() => DisposeCertificates(_certificates);

    private static void DisposeCertificates(X509Certificate2Collection certificates)
    {
        foreach (var certificate in certificates) certificate.Dispose();
    }

    private static bool IsAuthority(X509Certificate2 certificate)
    {
        foreach (var extension in certificate.Extensions)
            if (extension is X509BasicConstraintsExtension constraints) return constraints.CertificateAuthority;
        return false;
    }

    private static bool HasServerRole(X509Certificate2 certificate, bool requireServerOnly)
    {
        var leaf = false;
        var usage = false;
        var signature = false;
        var extensions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid?.Value is not { } oid || !extensions.Add(oid)) return false;
            if (extension is X509BasicConstraintsExtension constraints) leaf = !constraints.CertificateAuthority;
            if (extension is X509EnhancedKeyUsageExtension usages)
                foreach (Oid value in usages.EnhancedKeyUsages)
                {
                    if (requireServerOnly && string.Equals(value.Value, "1.3.6.1.5.5.7.3.2", StringComparison.Ordinal)) return false;
                    if (string.Equals(value.Value, "1.3.6.1.5.5.7.3.1", StringComparison.Ordinal)) usage = true;
                }
            if (extension is X509KeyUsageExtension key) signature = (key.KeyUsages & X509KeyUsageFlags.DigitalSignature) != X509KeyUsageFlags.None;
        }
        return leaf && usage && signature;
    }
}
