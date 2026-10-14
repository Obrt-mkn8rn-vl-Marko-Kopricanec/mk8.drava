using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Administration.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class ProtectedManualAmbientTests
{
    [Fact]
    public async Task BackupValidationUsesProtectedConfigurationWithoutRequiringLegacyDirectoriesAsync()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("protected-backup-route", context.RequestAborted)).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, enrolledSite: true, administration: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var bootstrap = await PortableConfiguration.LoadDocumentAsync<ApplicationBootstrap>(proxy.ApplicationBootstrapPath, CancellationToken.None).ConfigureAwait(true);
        // The fixture captures these generated drafts into the protected role before starting either process.
        Directory.Delete(Path.Combine(bootstrap.StateDirectory, "config"), recursive: true);
        Assert.False(Directory.Exists(Path.Combine(bootstrap.StateDirectory, "config")));
        Assert.False(Directory.Exists(Path.Combine(bootstrap.StateDirectory, "certs")));
        using var administrator = await AdministrationProcessTests.CreateAdministratorAsync(proxy).ConfigureAwait(true);
        using var response = await administrator.Client.PostAsync(new Uri("/admin/proxy/backup/validate", UriKind.Relative), content: null).ConfigureAwait(true);
        var result = await response.Content.ReadFromJsonAsync<ProxyRestoreValidationResponseBody>().ConfigureAwait(true);
        Assert.NotNull(result);
        Assert.Empty(result.Errors);
        Assert.True(result.ConfigValidationSucceeded);
        Assert.True(result.Succeeded);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(result.Manifest.Directories, static directory => directory.RelativePath.StartsWith("config", StringComparison.Ordinal));
        await proxy.WriteManualRouteAsync(upstream.Port, listenerPort: -1).ConfigureAwait(true);
        using var rejected = await administrator.Client.PostAsync(new Uri("/admin/proxy/backup/validate", UriKind.Relative), content: null).ConfigureAwait(true);
        var invalid = await rejected.Content.ReadFromJsonAsync<ProxyRestoreValidationResponseBody>().ConfigureAwait(true);
        Assert.NotNull(invalid);
        Assert.False(invalid.ConfigValidationSucceeded);
        Assert.False(invalid.Succeeded);
        Assert.NotEmpty(invalid.Errors);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var route = await proxy.Client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal("protected-backup-route", await route.Content.ReadAsStringAsync().ConfigureAwait(true));
        Assert.False(Directory.Exists(Path.Combine(bootstrap.StateDirectory, "config")));
        Assert.False(Directory.Exists(Path.Combine(bootstrap.StateDirectory, "certs")));
    }

    [Fact]
    public async Task ConflictingEnvironmentAndRawConfigurationCannotReplaceProtectedRoutesOrAdministratorAsync()
    {
        var directory = Directory.CreateTempSubdirectory("drava_manual_canary_").FullName;
        try
        {
            var config = Directory.CreateDirectory(Path.Combine(directory, "config")).FullName;
            await File.WriteAllTextAsync(Path.Combine(config, "proxy.json"), "invalid-synthetic-ambient-configuration", CancellationToken.None).ConfigureAwait(true);
            const string canaryToken = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["MDRAVA_DATA_DIR"] = directory, ["MDRAVA_ADMIN_TOKEN"] = canaryToken,
                ["Mdrava__DataDirectory"] = directory, ["ASPNETCORE_ENVIRONMENT"] = "Development",
            };
            var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("protected-manual-route", context.RequestAborted)).ConfigureAwait(true);
            await using var upstreamLifetime = upstream.ConfigureAwait(true);
            var proxy = await TwoProcessProxy.StartAsync(upstream.Port, enrolledSite: true, administration: true, processEnvironment: environment).ConfigureAwait(true);
            await using var proxyLifetime = proxy.ConfigureAwait(true);
            using var route = await proxy.Client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, route.StatusCode);
            Assert.Equal("protected-manual-route", await route.Content.ReadAsStringAsync().ConfigureAwait(true));
            using var administrator = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.ManagementPort, "admin.site.test", proxy.NodeCertificatePath);
            administrator.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", canaryToken);
            using var refused = await administrator.Client.GetAsync(new Uri("/admin/proxy/status", UriKind.Relative)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            administrator.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", proxy.AdministratorToken.RequireToken());
            using var allowed = await administrator.Client.GetAsync(new Uri("/admin/proxy/status", UriKind.Relative)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Equal([Path.Combine(config, "proxy.json")], Directory.GetFiles(config));
            Assert.DoesNotContain(canaryToken, proxy.ApplicationLog, StringComparison.Ordinal);
            Assert.DoesNotContain(canaryToken, proxy.GatewayLog, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
