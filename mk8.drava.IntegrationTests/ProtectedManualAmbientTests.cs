using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class ProtectedManualAmbientTests
{
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
