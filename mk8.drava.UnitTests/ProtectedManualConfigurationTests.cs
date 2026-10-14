using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Configuration;
using Supprocom.Secrets;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed partial class ProtectedManualConfigurationTests
{
    [Fact]
    public async Task AbsentProtectedInputIgnoresRawOperationalFilesAndDoesNotCreateALayoutAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var state = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(state.Path, state.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = role.ConfigureAwait(true);
        var loader = CreateLoader(state.Path, role);
        var loaded = Assert.IsType<ProxyConfigurationLoadResult.LoadedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Empty(loaded.Snapshot.Routes);
        Assert.False(Directory.Exists(Path.Combine(state.Path, "config")));
        var config = Directory.CreateDirectory(Path.Combine(state.Path, "config")).FullName;
        await File.WriteAllTextAsync(Path.Combine(config, "proxy.json"), "malformed raw configuration", CancellationToken.None).ConfigureAwait(true);
        var again = Assert.IsType<ProxyConfigurationLoadResult.LoadedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(2, again.Snapshot.Version);
        Assert.Empty(again.Snapshot.Routes);
        Assert.False(Directory.Exists(Path.Combine(config, "sites")));
    }

    [Theory]
    [InlineData("{\"certificates\":[{\"id\":\"site\",\"path\":\"/missing/raw.pfx\"}]}")]
    [InlineData("{\"certificates\":[{\"id\":\"site\",\"passwordEnvironmentVariable\":\"SYNTHETIC_SECRET\"}]}")]
    [InlineData("{\"admin\":{\"token\":\"synthetic-override-token\"}}")]
    [InlineData("{\"admin\":{\"tokenEnvironmentVariable\":\"SYNTHETIC_SECRET\"}}")]
    [InlineData("{\"admin\":{\"urls\":null}}")]
    [InlineData("{\"acme\":{\"contactEmails\":null}}")]
    [InlineData("{\"acme\":{\"certificates\":null}}")]
    [InlineData("{\"acme\":{\"certificates\":[null]}}")]
    [InlineData("{\"acme\":{\"certificates\":[{\"id\":\"site\",\"domains\":null}]}}")]
    [InlineData("{\"observability\":{\"logPersistence\":null}}")]
    [InlineData("{\"forwardedHeaders\":{\"trustedProxies\":null}}")]
    [InlineData("{\"certificates\":[null]}")]
    public async Task RetiredCredentialLocatorsRefuseWithoutConsumingAVersionAsync(string operational)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var state = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(state.Path, state.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = role.ConfigureAwait(true);
        var loader = CreateLoader(state.Path, role);
        await WriteInputAsync(state.Path, operational).ConfigureAwait(true);
        var invalid = Assert.IsType<ProxyConfigurationLoadResult.FailedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, invalid.WouldBeVersion);
        Assert.DoesNotContain("synthetic-override-token", string.Join(' ', invalid.Errors), StringComparison.Ordinal);
        await WriteInputAsync(state.Path, "{}").ConfigureAwait(true);
        var validated = Assert.IsType<ProxyConfigurationLoadResult.ValidatedResult>(await loader.ValidateAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, validated.WouldBeVersion);
        var healthy = Assert.IsType<ProxyConfigurationLoadResult.LoadedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, healthy.Snapshot.Version);
        Assert.False(Directory.Exists(Path.Combine(state.Path, "config")));
    }

    [Fact]
    public async Task ProtectedOperationalRefreshRetainsAnImmutableOlderSnapshotAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var state = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(state.Path, state.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = role.ConfigureAwait(true);
        var loader = CreateLoader(state.Path, role);
        await WriteInputAsync(state.Path, "{\"timeouts\":{\"upstreamConnectTimeoutMs\":2345}}").ConfigureAwait(true);
        var first = Assert.IsType<ProxyConfigurationLoadResult.LoadedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        await WriteInputAsync(state.Path, "{\"timeouts\":{\"upstreamConnectTimeoutMs\":3456}}").ConfigureAwait(true);
        var second = Assert.IsType<ProxyConfigurationLoadResult.LoadedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(TimeSpan.FromMilliseconds(2345), first.Snapshot.Timeouts.UpstreamConnectTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(3456), second.Snapshot.Timeouts.UpstreamConnectTimeout);
        Assert.Equal("[redacted]", role.ReadManualConfiguration().ToString());
        Assert.Equal("not-configured", second.Snapshot.AdminSecurity.TokenSource);
    }

    private static async Task WriteInputAsync(string state, string operational)
    {
        var document = PortableManualConfigurationSource.Encode("synthetic-site", operational, [], []);
        var store = PortablePolicyPayloadStoreTests.Store(state);
        await store.UpdateDocumentAsync(settings =>
        [
            .. settings.Where(static item => !string.Equals(item.Key,
                        PortableManualConfigurationSource.CollectionKey, StringComparison.OrdinalIgnoreCase)),
            new SupprocomSecretSetting(PortableManualConfigurationSource.CollectionKey, document.Reveal()),
        ], CancellationToken.None).ConfigureAwait(false);
    }

    [Theory]
    [InlineData(ProxyManualSiteFormat.Json)]
    [InlineData(ProxyManualSiteFormat.Yaml)]
    public async Task ProtectedSiteAndPasswordBoundPfxProduceRoutesWithoutRawFilesAsync(ProxyManualSiteFormat format)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var state = new RegistryStateDirectory();
        using var material = DevelopmentUpstreamTrustCertificates.Create();
        const string password = "synthetic-manual-password";
        var bytes = material.Leaf.Export(X509ContentType.Pkcs12, password);
        PortableManualConfigurationDocument document;
        try
        {
            document = PortableManualConfigurationSource.Encode("synthetic-site",
                "{\"certificates\":[{\"id\":\"site\",\"password\":\"" + password + "\"}]}",
                [new ProxyManualSiteInput("manual", format, SiteDocument(format))], [new ProxyManualCertificateInput("site", bytes)]);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(state.Path, state.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = role.ConfigureAwait(true);
        await WriteDocumentAsync(state.Path, document).ConfigureAwait(true);
        var loader = CreateLoader(state.Path, role);
        var loaded = Assert.IsType<ProxyConfigurationLoadResult.LoadedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        using var certificate = loaded.Snapshot.Certificates["site"].Certificate;
        Assert.Equal(material.Leaf.Thumbprint, certificate.Thumbprint);
        Assert.True(certificate.HasPrivateKey);
        Assert.Equal("manual", UpstreamTrustConfigurationTests.Only(loaded.Snapshot.Routes).SiteName);
        Assert.Equal("site", UpstreamTrustConfigurationTests.Only(loaded.Snapshot.Listeners).DefaultCertificateId);
        Assert.False(Directory.Exists(Path.Combine(state.Path, "config")));
        Assert.False(Directory.Exists(Path.Combine(state.Path, "certs")));
        Assert.DoesNotContain(password, role.ReadManualConfiguration().ToString(), StringComparison.Ordinal);
    }

    private static string SiteDocument(ProxyManualSiteFormat format) => format == ProxyManualSiteFormat.Json
        ? """
          {"name":"manual","host":"manual.test","listeners":[{"name":"https","address":"127.0.0.1","port":8443,"transport":"https","defaultCertificateId":"site"}],"pathPrefix":"/","upstreams":[{"name":"backend","address":"127.0.0.1","port":9000}]}
          """
        : """
          name: manual
          host: manual.test
          listeners:
            - name: https
              address: 127.0.0.1
              port: 8443
              transport: https
              defaultCertificateId: site
          pathPrefix: /
          upstreams:
            - name: backend
              address: 127.0.0.1
              port: 9000
          """;

    private static async Task WriteDocumentAsync(string state, PortableManualConfigurationDocument document)
    {
        var store = PortablePolicyPayloadStoreTests.Store(state);
        await store.UpdateDocumentAsync(settings =>
        [
            .. settings.Where(static item => !string.Equals(item.Key,
                        PortableManualConfigurationSource.CollectionKey, StringComparison.OrdinalIgnoreCase)),
            new SupprocomSecretSetting(PortableManualConfigurationSource.CollectionKey, document.Reveal()),
        ], CancellationToken.None).ConfigureAwait(false);
    }

    private static ProxyConfigurationLoader CreateLoader(string state, PortableRole<ApplicationBootstrap> role)
    {
        var paths = new ApplicationDataDirectoryProvider(state);
        return new ProxyConfigurationLoader(paths, new ProxyDataDirectoryBootstrapper(paths), new SiteConfigurationParser(),
            new ProxyAdminUrlPolicy(), new ProxyEndpointAddressPolicy(), new ProxyRelativeStoragePathPolicy(), new ProxyUrlSyntaxPolicy(),
            new ProxyForwardedHeadersAddressPolicy(), NullLogger<ProxyConfigurationLoader>.Instance, TimeProvider.System,
            upstreamTrust: null, new PortableManualConfigurationSource(role));
    }
}
