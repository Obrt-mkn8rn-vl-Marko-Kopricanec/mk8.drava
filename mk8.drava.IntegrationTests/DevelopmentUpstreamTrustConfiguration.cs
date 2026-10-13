using System.Security.Cryptography;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Configuration;
using Mk8.Drava.UnitTests;

namespace Mk8.Drava.IntegrationTests;

internal static class DevelopmentUpstreamTrustConfiguration
{
    public const string MaterialId = "upstream-root";

    public static IReadOnlyList<UpstreamTrustMaterial> Entries(DevelopmentUpstreamTrustCertificates certificates) =>
        [new UpstreamTrustMaterial { Id = MaterialId, Certificate = new SecretBytes(certificates.Root.RawData) }];

    public static UpstreamTlsOptions Policy(DevelopmentUpstreamTrustCertificates certificates) => new()
    {
        ValidateCertificate = true, SniHost = "backend.drava.invalid",
        TrustedRoot = new TrustedRootCertificateOptions
        {
            MaterialId = MaterialId, Sha256 = certificates.Root.GetCertHashString(HashAlgorithmName.SHA256),
        },
    };

    public static async Task<PortableUpstreamTrustMaterialSource> LoadSourceAsync(string directory, DevelopmentUpstreamTrustCertificates certificates)
    {
        var path = Path.Combine(directory, ".env");
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(new ApplicationBootstrap
        {
            UpstreamTrust = Entries(certificates),
        })).ConfigureAwait(false);
        var bootstrap = await PortableConfiguration.LoadDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(false);
        return new PortableUpstreamTrustMaterialSource(bootstrap);
    }
}
