using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class TestCertificates
{
    // Pin only the generated leaf for this owned loopback fixture; no system trust-store mutation.
    public static RemoteCertificateValidationCallback PinServerCertificate(string path, string? password = null, bool allowNameMismatch = false)
    {
        using var expected = X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
        var fingerprint = expected.GetCertHash(HashAlgorithmName.SHA256);
        var notBefore = expected.NotBefore.ToUniversalTime();
        var notAfter = expected.NotAfter.ToUniversalTime();
        var allowedErrors = SslPolicyErrors.RemoteCertificateChainErrors;
        if (allowNameMismatch) allowedErrors |= SslPolicyErrors.RemoteCertificateNameMismatch;
        return (_, certificate, _, errors) =>
        {
            if (certificate is null || (errors & ~allowedErrors) != SslPolicyErrors.None) return false;
            var now = DateTime.UtcNow;
            return now >= notBefore && now <= notAfter
                && CryptographicOperations.FixedTimeEquals(fingerprint, certificate.GetCertHash(HashAlgorithmName.SHA256));
        };
    }

    public static void WriteSelfSignedPfx(string path, string subjectName, string? password = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, CreateSelfSignedPfxBytes(subjectName, password));
    }

    public static byte[] CreateSelfSignedPfxBytes(string subjectName, string? password = null, int validDays = 30)
    {
        return CreateSelfSignedPfxBytesForValidity(subjectName, password, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(validDays));
    }

    public static byte[] CreateSelfSignedPfxBytesForValidity(string subjectName, string? password, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={subjectName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(subjectName);
        request.CertificateExtensions.Add(sanBuilder.Build());
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        return certificate.Export(X509ContentType.Pfx, password);
    }
}
