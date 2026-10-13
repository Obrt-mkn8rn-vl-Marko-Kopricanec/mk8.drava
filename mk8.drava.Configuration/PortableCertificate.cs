using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Mk8.Drava.Configuration;

public static class PortableCertificate
{
    public static X509Certificate2 LoadAuthority(SecretBytes material, string expectedFingerprint, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        if (expectedFingerprint.Length != 64 || expectedFingerprint.Any(static character => character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
            throw new InvalidDataException("Invalid enrolled issuer fingerprint.");
        var issuer = LoadPrivate(material);
        try
        {
            using var key = issuer.GetECDsaPrivateKey();
            if (key is null || key.KeySize != 256 || !string.Equals(key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value, "1.2.840.10045.3.1.7", StringComparison.Ordinal) ||
                !string.Equals(issuer.GetCertHashString(HashAlgorithmName.SHA256), expectedFingerprint, StringComparison.Ordinal) ||
                !IsAuthority(issuer) || clock.GetUtcNow().UtcDateTime < issuer.NotBefore.ToUniversalTime() || clock.GetUtcNow().UtcDateTime >= issuer.NotAfter.ToUniversalTime())
            {
                throw new InvalidDataException("Site issuer identity, role, key or lifetime is invalid.");
            }

            return issuer;
        }
        catch { issuer.Dispose(); throw; }
    }

    public static X509Certificate2 LoadPublic(SecretBytes material)
    {
        ArgumentNullException.ThrowIfNull(material);
        var bytes = material.Reveal(1, 65536);
        try { return X509CertificateLoader.LoadCertificate(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static X509Certificate2 LoadPrivate(SecretBytes material)
    {
        ArgumentNullException.ThrowIfNull(material);
        var bytes = material.Reveal(128, 65536);
        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12(bytes, password: null, X509KeyStorageFlags.EphemeralKeySet);
            if (certificate.HasPrivateKey) return certificate;
            certificate.Dispose();
            throw new InvalidDataException("Protected certificate requires its private key.");
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static SecretBytes ExportPrivate(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.HasPrivateKey) throw new InvalidDataException("Protected certificate requires its private key.");
        var bytes = certificate.Export(X509ContentType.Pkcs12);
        try { return new SecretBytes(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
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
            if (extension is X509KeyUsageExtension usage) signing |= usage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign);
        }
        return authority && signing;
    }
}
