using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed partial class ProtectedManualConfigurationTests
{
    [Theory]
    [InlineData("{\"listeners\":null}")]
    [InlineData("{\"listeners\":[null]}")]
    [InlineData("{\"listeners\":[{\"sniCertificates\":[null]}]}")]
    [InlineData("{\"routes\":null}")]
    [InlineData("{\"routes\":[null]}")]
    [InlineData("{\"routes\":[{\"healthCheck\":null}]}")]
    [InlineData("{\"upstreams\":[null]}")]
    [InlineData("{\"upstreams\":[{\"upstreamTls\":null}]}")]
    [InlineData("{\"headerPolicy\":null}")]
    [InlineData("{\"headerPolicy\":{\"setRequestHeaders\":[null]}}")]
    [InlineData("{\"cache\":{\"varyByHeaders\":null}}")]
    [InlineData("{\"cache\":{\"methods\":[null]}}")]
    public async Task NullSiteMembersRefuseWithoutThrowingOrConsumingAVersionAsync(string site)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var state = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(state.Path, state.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = role.ConfigureAwait(true);
        var loader = CreateLoader(state.Path, role);
        var document = PortableManualConfigurationSource.Encode("synthetic-site", "{}",
            [new ProxyManualSiteInput("invalid", ProxyManualSiteFormat.Json, site)], []);
        await WriteDocumentAsync(state.Path, document).ConfigureAwait(true);
        var failed = Assert.IsType<ProxyConfigurationLoadResult.FailedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, failed.WouldBeVersion);
        Assert.Contains(failed.FileErrors, static error => error.Message.Contains("Protected site configuration is invalid.", StringComparison.Ordinal));
        await WriteInputAsync(state.Path, "{}").ConfigureAwait(true);
        var healthy = Assert.IsType<ProxyConfigurationLoadResult.LoadedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, healthy.Snapshot.Version);
    }

    [Theory]
    [InlineData("missing-schema")]
    [InlineData("wrong-schema")]
    [InlineData("wrong-site")]
    [InlineData("duplicate-field")]
    [InlineData("unknown-field")]
    [InlineData("null-sites")]
    [InlineData("site-name")]
    [InlineData("duplicate-sites")]
    [InlineData("pfx-padding")]
    public async Task MalformedEnvelopeRefusesWithoutPoisoningHealthyReloadAsync(string fault)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var state = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(state.Path, state.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = role.ConfigureAwait(true);
        var loader = CreateLoader(state.Path, role);
        await WriteDocumentAsync(state.Path, BrokenEnvelope(fault)).ConfigureAwait(true);
        var failed = Assert.IsType<ProxyConfigurationLoadResult.FailedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, failed.WouldBeVersion);
        Assert.DoesNotContain("synthetic-private-canary", string.Join(' ', failed.Errors), StringComparison.Ordinal);
        await WriteInputAsync(state.Path, "{}").ConfigureAwait(true);
        var healthy = Assert.IsType<ProxyConfigurationLoadResult.LoadedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, healthy.Snapshot.Version);
        Assert.Empty(healthy.Snapshot.Routes);
    }

    private static PortableManualConfigurationDocument BrokenEnvelope(string fault)
    {
        const string valid = "{\"schemaVersion\":1,\"siteId\":\"synthetic-site\",\"operationalDocument\":\"{}\",\"sites\":[],\"certificates\":{}}";
        var root = JsonNode.Parse(valid)?.AsObject() ?? throw new InvalidOperationException("Missing synthetic envelope.");
        switch (fault)
        {
            case "missing-schema": root.Remove("schemaVersion"); break;
            case "wrong-schema": root["schemaVersion"] = 2; break;
            case "wrong-site": root["siteId"] = "foreign-site"; break;
            case "duplicate-field": return new PortableManualConfigurationDocument(valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal));
            case "unknown-field": root["private"] = "synthetic-private-canary"; break;
            case "null-sites": root["sites"] = null; break;
            case "site-name": root["sites"] = JsonSerializer.SerializeToNode(new[] { new { name = "../outside", format = "json", document = "{}" } }); break;
            case "duplicate-sites": root["sites"] = JsonSerializer.SerializeToNode(new[]
                { new { name = "site.json", format = "json", document = "{}" }, new { name = "SITE.json", format = "json", document = "{}" } }); break;
            case "pfx-padding": root["certificates"] = JsonNode.Parse("{\"site\":\"AB==\"}"); break;
            default: throw new InvalidOperationException("Unknown synthetic envelope fault.");
        }
        return new PortableManualConfigurationDocument(root.ToJsonString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("password")]
    [InlineData("public-only")]
    public async Task FailedCertificateLoadRetainsOlderNativeCertificateAndAllowsLaterHealthyInputAsync(string fault)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var state = new RegistryStateDirectory();
        using var material = DevelopmentUpstreamTrustCertificates.Create();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(state.Path, state.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = role.ConfigureAwait(true);
        var loader = CreateLoader(state.Path, role);
        await WriteDocumentAsync(state.Path, CertificateDocument(material.Leaf, publicOnly: false, password: "synthetic-password", include: true)).ConfigureAwait(true);
        var first = Assert.IsType<ProxyConfigurationLoadResult.LoadedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        using var retained = first.Snapshot.Certificates["site"].Certificate;
        await WriteDocumentAsync(state.Path, CertificateDocument(material.Leaf, string.Equals(fault, "public-only", StringComparison.Ordinal),
            string.Equals(fault, "password", StringComparison.Ordinal) ? "wrong-password" : "synthetic-password", !string.Equals(fault, "missing", StringComparison.Ordinal))).ConfigureAwait(true);
        var failed = Assert.IsType<ProxyConfigurationLoadResult.FailedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(2, failed.WouldBeVersion);
        Assert.True(retained.HasPrivateKey);
        Assert.Equal(material.Leaf.Thumbprint, retained.Thumbprint);
        await WriteInputAsync(state.Path, "{}").ConfigureAwait(true);
        var healthy = Assert.IsType<ProxyConfigurationLoadResult.LoadedResult>(await loader.LoadAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(2, healthy.Snapshot.Version);
        Assert.Empty(healthy.Snapshot.Certificates);
    }

    private static PortableManualConfigurationDocument CertificateDocument(X509Certificate2 material, bool publicOnly, string password, bool include)
    {
        using var publicCertificate = X509CertificateLoader.LoadCertificate(material.RawData);
        var bytes = (publicOnly ? publicCertificate : material).Export(X509ContentType.Pkcs12, "synthetic-password");
        try
        {
            return PortableManualConfigurationSource.Encode("synthetic-site", JsonSerializer.Serialize(new
            { certificates = new[] { new { id = "site", password } }, }), [], include ? [new ProxyManualCertificateInput("site", bytes)] : []);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
