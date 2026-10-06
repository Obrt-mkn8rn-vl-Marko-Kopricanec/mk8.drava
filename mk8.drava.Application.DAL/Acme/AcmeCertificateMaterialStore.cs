using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Configuration.Loading;

namespace Mk8.Drava.Application.DAL.Acme;
public static class AcmeCertificateMaterialStore
{
    public const int MaximumPfxBytes = 1024 * 1024;
    public static IReadOnlyDictionary<string, RuntimeCertificate> LoadRuntimeCertificates(ProxyAcmeOptions options, string dataDirectory, List<ProxyConfigurationFileError> errors)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(errors);
        Dictionary<string, RuntimeCertificate> certificates = new(StringComparer.OrdinalIgnoreCase);
        if (!options.Enabled)
        {
            return certificates;
        }

        var layout = GetLayout(dataDirectory, options.StoragePath);
        foreach (var certificateOptions in options.Certificates.Where(static certificate => certificate.Enabled))
        {
            var pfxPath = GetPrivateKeyPfxPath(layout, certificateOptions.Id);
            if (!File.Exists(pfxPath))
            {
                continue;
            }

            try
            {
                var certificate = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, ReadOnlySpan<char>.Empty, X509KeyStorageFlags.UserKeySet);
                if (!certificate.HasPrivateKey)
                {
                    certificate.Dispose();
                    errors.Add(ProxyConfigurationFileError.Global($"ACME certificate '{certificateOptions.Id}' must contain a private key."));
                    continue;
                }

                certificates[certificateOptions.Id] = RuntimeCertificateFactory.Acme(certificateOptions.Id, certificate, certificateOptions.Domains);
            }
            catch (CryptographicException exception)
            {
                errors.Add(ProxyConfigurationFileError.Global($"ACME certificate '{certificateOptions.Id}' could not be loaded: {exception.Message}"));
            }
        }

        return certificates;
    }

    public static RuntimeCertificate WriteAndLoad(AcmeCertificateMaterialWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pfxBytes = request.PfxBytes;
        if (pfxBytes.Length is 0 or > MaximumPfxBytes)
        {
            throw new InvalidOperationException($"ACME certificate '{request.CertificateId}' PFX payload was empty or too large.");
        }

        var layout = EnsureLayout(request.DataDirectory, request.StoragePath);
        var pfxPath = GetPrivateKeyPfxPath(layout, request.CertificateId);
        var certificate = X509CertificateLoader.LoadPkcs12(pfxBytes, ReadOnlySpan<char>.Empty, X509KeyStorageFlags.UserKeySet);
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"ACME certificate '{request.CertificateId}' must contain a private key.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(pfxPath)!);
        File.WriteAllBytes(pfxPath, pfxBytes);
        var certificatePemPath = GetCertificatePemPath(layout, request.CertificateId);
        Directory.CreateDirectory(Path.GetDirectoryName(certificatePemPath)!);
        File.WriteAllText(certificatePemPath, certificate.ExportCertificatePem());
        var metadataPath = GetMetadataPath(layout, request.CertificateId);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        var metadata = new AcmeCertificateMetadata(request.CertificateId, request.Domains, request.WrittenAtUtc, certificate.NotBefore, certificate.NotAfter, certificate.Thumbprint);
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, SiteConfigurationParser.WriteJsonOptions));
        return RuntimeCertificateFactory.Acme(request.CertificateId, certificate, request.Domains);
    }

    public static AcmeCertificateStorageLayout EnsureLayout(string dataDirectory, string storagePath)
    {
        var layout = GetLayout(dataDirectory, storagePath);
        Directory.CreateDirectory(layout.Root);
        Directory.CreateDirectory(layout.AccountsDirectory);
        Directory.CreateDirectory(layout.CertificatesDirectory);
        Directory.CreateDirectory(layout.PrivateKeysDirectory);
        Directory.CreateDirectory(layout.MetadataDirectory);
        return layout;
    }

    public static AcmeCertificateStorageLayout GetLayout(string dataDirectory, string storagePath)
    {
        var root = Path.Combine(dataDirectory, "certs", storagePath);
        return new AcmeCertificateStorageLayout(root, Path.Combine(root, "accounts"), Path.Combine(root, "certificates"), Path.Combine(root, "private-keys"), Path.Combine(root, "metadata"));
    }

    public static string GetPrivateKeyPfxPath(AcmeCertificateStorageLayout layout, string certificateId)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Path.Combine(layout.PrivateKeysDirectory, SafeSegment(certificateId), "current.pfx");
    }

    public static string GetCertificatePemPath(AcmeCertificateStorageLayout layout, string certificateId)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Path.Combine(layout.CertificatesDirectory, SafeSegment(certificateId), "certificate.pem");
    }

    public static string GetMetadataPath(AcmeCertificateStorageLayout layout, string certificateId)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Path.Combine(layout.MetadataDirectory, SafeSegment(certificateId), "status.json");
    }

    private static string SafeSegment(string value)
    {
        return string.Concat(value.Select(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_'));
    }
}
