using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.DAL.Configuration.Loading;

// Captured once from the Application role that also owns the startup credentials.
public sealed class PortableUpstreamTrustMaterialSource : IUpstreamTrustMaterialSource
{
    private readonly Dictionary<string, RuntimeTrustedRootCertificate> _roots = new(StringComparer.Ordinal);

    public PortableUpstreamTrustMaterialSource(ApplicationBootstrap bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        if (bootstrap.UpstreamTrust is null || bootstrap.UpstreamTrust.Count > 64)
            throw new InvalidDataException("Upstream trust material exceeds its collection bound.");
        foreach (var material in bootstrap.UpstreamTrust)
        {
            if (material is null) throw new InvalidDataException("Upstream trust material cannot contain null entries.");
            material.Validate();
            var root = Capture(material);
            if (!_roots.TryAdd(material.Id, root)) throw new InvalidDataException("Duplicate upstream trust material identifier.");
        }
    }

    public RuntimeTrustedRootCertificate Resolve(string materialId, string sha256)
    {
        RuntimeTrustedRootCertificate.ValidateReference(materialId, sha256);
        if (!_roots.TryGetValue(materialId, out var root)) throw new InvalidDataException("The configured upstream trust material is absent from the startup snapshot.");
        if (!string.Equals(root.Sha256, sha256, StringComparison.Ordinal)) throw new InvalidDataException("The configured upstream trust material does not match its pinned fingerprint.");
        return root;
    }

    private static RuntimeTrustedRootCertificate Capture(UpstreamTrustMaterial material)
    {
        try
        {
            using var certificate = PortableCertificate.LoadPublic(material.Certificate);
            RequireAuthority(certificate);
            var bytes = certificate.RawData;
            try { return new RuntimeTrustedRootCertificate(material.Id, certificate.GetCertHashString(HashAlgorithmName.SHA256), bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("Upstream trust material must contain a public signing authority.", exception);
        }
    }

    private static void RequireAuthority(X509Certificate2 certificate)
    {
        if (certificate.HasPrivateKey) throw new InvalidDataException("Upstream trust material cannot contain a private key.");
        X509BasicConstraintsExtension? constraints = null;
        X509KeyUsageExtension? usage = null;
        foreach (var extension in certificate.Extensions)
        {
            if (extension is X509BasicConstraintsExtension basic)
            {
                if (constraints is not null) throw new InvalidDataException("Duplicate CA constraints.");
                constraints = basic;
            }
            if (extension is X509KeyUsageExtension keyUsage)
            {
                if (usage is not null) throw new InvalidDataException("Duplicate CA key usage.");
                usage = keyUsage;
            }
        }
        if (constraints is not { CertificateAuthority: true } || usage?.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign) != true)
            throw new InvalidDataException("Upstream trust material must contain a public signing authority.");
    }
}
