using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PortableUpstreamConfigurationLoadTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnresolvedTrustFailsValidationAndLoadWithoutConsumingAVersionAsync(bool missingMaterial)
    {
        using var directory = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var fingerprint = certificates.Root.GetCertHashString(HashAlgorithmName.SHA256);
        var source = new PortableUpstreamTrustMaterialSource(new ApplicationBootstrap
        {
            UpstreamTrust = [new UpstreamTrustMaterial { Id = "operator-root", Certificate = new SecretBytes(certificates.Root.RawData) }],
        });
        var loader = CreateLoader(directory.Path, source);
        var invalid = SiteText(missingMaterial ? "missing-root" : "operator-root", missingMaterial ? fingerprint : new string('0', 64));
        await WriteSiteAsync(directory.Path, invalid).ConfigureAwait(true);

        var validation = Assert.IsType<ProxyConfigurationLoadResult.FailedResult>(await loader.ValidateAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, validation.WouldBeVersion);
        Assert.Contains(validation.Errors, static error => error.Contains("trust material", StringComparison.Ordinal));
        var failure = Assert.IsType<ProxyConfigurationLoadResult.FailedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, failure.WouldBeVersion);
        Assert.Contains(failure.Errors, static error => error.Contains("trust material", StringComparison.Ordinal));

        await WriteSiteAsync(directory.Path, SiteText("operator-root", fingerprint)).ConfigureAwait(true);
        var valid = Assert.IsType<ProxyConfigurationLoadResult.ValidatedResult>(await loader.ValidateAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, valid.WouldBeVersion);
        var loaded = Assert.IsType<ProxyConfigurationLoadResult.LoadedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, loaded.Snapshot.Version);
        Assert.Equal(certificates.Root.RawData, ReadRoot(loaded.Snapshot).CopyCertificate());
    }

    [Fact]
    public async Task ReloadUsesCapturedTrustEvenAfterTheBootstrapMaterialListChangesAsync()
    {
        using var directory = new RegistryStateDirectory();
        using var original = DevelopmentUpstreamTrustCertificates.Create();
        using var replacement = DevelopmentUpstreamTrustCertificates.Create();
        var fingerprint = original.Root.GetCertHashString(HashAlgorithmName.SHA256);
        var materials = new List<UpstreamTrustMaterial>
        {
            new() { Id = "operator-root", Certificate = new SecretBytes(original.Root.RawData) },
        };
        var bootstrap = new ApplicationBootstrap { UpstreamTrust = materials };
        var loader = CreateLoader(directory.Path, new PortableUpstreamTrustMaterialSource(bootstrap));
        await WriteSiteAsync(directory.Path, SiteText("operator-root", fingerprint)).ConfigureAwait(true);
        var first = Assert.IsType<ProxyConfigurationLoadResult.LoadedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        materials[0] = new UpstreamTrustMaterial { Id = "operator-root", Certificate = new SecretBytes(replacement.Root.RawData) };

        var second = Assert.IsType<ProxyConfigurationLoadResult.LoadedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(2, second.Snapshot.Version);
        Assert.Equal(original.Root.RawData, ReadRoot(second.Snapshot).CopyCertificate());
        Assert.Equal(ReadRoot(first.Snapshot).Identity, ReadRoot(second.Snapshot).Identity);

        var restarted = CreateLoader(directory.Path, new PortableUpstreamTrustMaterialSource(bootstrap));
        var rejected = Assert.IsType<ProxyConfigurationLoadResult.FailedResult>(await restarted.ValidateAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Contains(rejected.Errors, static error => error.Contains("pinned fingerprint", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SubmittedLintReportsUnresolvedTrustWithoutPublishingRuntimeRoutes(bool missingMaterial)
    {
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var fingerprint = certificates.Root.GetCertHashString(HashAlgorithmName.SHA256);
        var source = new PortableUpstreamTrustMaterialSource(new ApplicationBootstrap
        {
            UpstreamTrust = [new UpstreamTrustMaterial { Id = "operator-root", Certificate = new SecretBytes(certificates.Root.RawData) }],
        });
        var lint = new ProxyConfigLintSubmittedConfigurationSource(new SiteConfigurationParser(), new DisabledHttp3Support(),
            new ProxyEndpointAddressPolicy(), new ProxyUrlSyntaxPolicy(), source);
        var invalid = SiteText(missingMaterial ? "missing-root" : "operator-root", missingMaterial ? fingerprint : new string('0', 64));

        var result = Assert.IsType<ProxyConfigLintSubmittedConfigurationResult.LoadedResult>(lint.Read(invalid, ProxyConfigurationNormalizeFormat.Json, DateTimeOffset.UnixEpoch));
        Assert.Contains(result.ValidationErrors, static error => error.Message.Contains("trust material", StringComparison.Ordinal));
        Assert.Empty(result.Snapshot.Routes);

        var valid = Assert.IsType<ProxyConfigLintSubmittedConfigurationResult.LoadedResult>(lint.Read(SiteText("operator-root", fingerprint), ProxyConfigurationNormalizeFormat.Json, DateTimeOffset.UnixEpoch));
        Assert.Empty(valid.ValidationErrors);
        Assert.Equal("service", UpstreamTrustConfigurationTests.Only(valid.Snapshot.Routes).SiteName);
    }

    private static ProxyConfigurationLoader CreateLoader(string directory, IUpstreamTrustMaterialSource source)
    {
        var paths = new ApplicationDataDirectoryProvider(directory);
        return new ProxyConfigurationLoader(paths, new ProxyDataDirectoryBootstrapper(paths), new SiteConfigurationParser(),
            new ProxyAdminUrlPolicy(), new ProxyEndpointAddressPolicy(), new ProxyRelativeStoragePathPolicy(),
            new ProxyUrlSyntaxPolicy(), new ProxyForwardedHeadersAddressPolicy(), NullLogger<ProxyConfigurationLoader>.Instance,
            TimeProvider.System, source);
    }

    private static RuntimeTrustedRootCertificate ReadRoot(ProxyConfigurationSnapshot snapshot) =>
        Assert.IsType<RuntimeTrustedRootCertificate>(UpstreamTrustConfigurationTests.Only(
            UpstreamTrustConfigurationTests.Only(snapshot.Routes).Upstreams).Tls.TrustedRoot);

    private static async Task WriteSiteAsync(string directory, string text)
    {
        var sites = Directory.CreateDirectory(Path.Combine(directory, "config", "sites")).FullName;
        await File.WriteAllTextAsync(Path.Combine(sites, "service.json"), text).ConfigureAwait(false);
    }

    private static string SiteText(string materialId, string fingerprint) => JsonSerializer.Serialize(new SiteOptions
    {
        Name = "service", Host = "service.drava.invalid",
        Listeners = [new ListenerOptions { Name = "http", Address = "127.0.0.1", Port = 8080 }],
        Upstreams = [new UpstreamOptions
        {
            Name = "backend", Scheme = "https", Address = "127.0.0.1", Port = 8443,
            UpstreamTls = new UpstreamTlsOptions
            {
                ValidateCertificate = true, SniHost = "backend.drava.invalid",
                TrustedRoot = new TrustedRootCertificateOptions { MaterialId = materialId, Sha256 = fingerprint },
            },
        }],
    }, SiteConfigurationParser.WriteJsonOptions);

    private sealed class DisabledHttp3Support : IRuntimeHttp3PlatformSupportSource
    {
        public RuntimeHttp3PlatformSupport Read() => RuntimeHttp3PlatformSupport.FromFlags(quicListenerSupported: false, quicConnectionSupported: false);
    }
}
