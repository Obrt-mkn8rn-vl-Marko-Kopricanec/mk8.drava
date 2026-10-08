using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.Hosting;

internal static class AcmeServingCertificateCandidate
{
    public static ServingCertificate Prepare(ReadOnlyMemory<byte> issued, ControllerBootstrap controller)
    {
        if (issued.Length is < 128 or > 65536) throw new InvalidDataException("Issued serving material exceeds its bound.");
        var certificates = X509CertificateLoader.LoadPkcs12Collection(issued.Span, null, X509KeyStorageFlags.EphemeralKeySet,
            new Pkcs12LoaderLimits { MaxCertificates = 16, MaxKeys = 1 });
        try
        {
            X509Certificate2? leaf = null;
            foreach (var certificate in certificates)
                if (certificate.HasPrivateKey)
                {
                    if (leaf is not null) throw new InvalidDataException("Issued serving bundle contains multiple private keys.");
                    leaf = certificate;
                }
            if (leaf is null) throw new InvalidDataException("Issued serving bundle has no private leaf.");
            if (string.Equals(controller.ServingTrust.Mode, "pinned", StringComparison.Ordinal)) AppendPinnedRoot(certificates, controller);
            var bytes = certificates.Export(X509ContentType.Pkcs12) ?? throw new InvalidDataException("Issued serving bundle export failed.");
            if (bytes.Length > 65536) throw new InvalidDataException("Complete serving bundle exceeds its bound.");
            return new ServingCertificate { CertificateId = "site", Pfx = ByteString.CopyFrom(bytes),
                NotAfterUnixSeconds = new DateTimeOffset(leaf.NotAfter.ToUniversalTime()).ToUnixTimeSeconds(),
                HostNames = { "*." + controller.Domain, "register." + controller.Domain } };
        }
        finally { foreach (var certificate in certificates) certificate.Dispose(); }
    }

    private static void AppendPinnedRoot(X509Certificate2Collection certificates, ControllerBootstrap controller)
    {
        using var approved = X509CertificateLoader.LoadCertificate(PrivateCertificateFile.Read(controller.Acme.PinnedServingRootPath));
        if (!string.Equals(approved.GetCertHashString(HashAlgorithmName.SHA256), controller.ServingTrust.RootFingerprint, StringComparison.Ordinal) ||
            approved.HasPrivateKey || !IsAuthority(approved)) throw new InvalidDataException("Owner serving root does not match its approved authority fingerprint.");
        foreach (var certificate in certificates)
            if (string.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256), controller.ServingTrust.RootFingerprint, StringComparison.Ordinal)) return;
        if (certificates.Count == 16) throw new InvalidDataException("Issued bundle cannot add its approved root within the certificate bound.");
        certificates.Add(X509CertificateLoader.LoadCertificate(approved.RawData));
    }

    private static bool IsAuthority(X509Certificate2 certificate)
    {
        foreach (var extension in certificate.Extensions)
            if (extension is X509BasicConstraintsExtension constraints) return constraints.CertificateAuthority;
        return false;
    }
}
