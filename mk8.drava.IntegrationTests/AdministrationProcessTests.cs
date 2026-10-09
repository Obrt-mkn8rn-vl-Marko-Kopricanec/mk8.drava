using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Contracts.Administration.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class AdministrationProcessTests
{
    [Fact]
    public async Task ManagementRequiresBothSiteTlsAndApplicationAuthorizationAndIsNotExposedOnServingListenersAsync()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("business")).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, enrolledSite: true, administration: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var noCertificate = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.ManagementPort, "admin.site.test");
        await Assert.ThrowsAsync<HttpRequestException>(() => noCertificate.Client.GetAsync(new Uri("/admin/proxy/status", UriKind.Relative))).ConfigureAwait(true);
        using var administrator = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.ManagementPort, "admin.site.test", proxy.NodeCertificatePath);
        using var missing = await administrator.Client.GetAsync(new Uri("/admin/proxy/status", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Contains(missing.Headers.WwwAuthenticate, static value => string.Equals(value.Scheme, "Bearer", StringComparison.Ordinal));
        administrator.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new string('A', 64));
        using var wrong = await administrator.Client.GetAsync(new Uri("/admin/proxy/status", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        var token = await File.ReadAllTextAsync(proxy.AdministratorTokenPath).ConfigureAwait(true);
        administrator.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var allowed = await administrator.Client.GetAsync(new Uri("/admin/proxy/status", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var status = await allowed.Content.ReadFromJsonAsync<ProxyStatusResponse>().ConfigureAwait(true);
        Assert.NotNull(status);
        Assert.True(status.ConfiguredRoutes > 0);
        using var serving = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        serving.Client.DefaultRequestHeaders.Authorization = administrator.Client.DefaultRequestHeaders.Authorization;
        using var isolated = await serving.Client.GetAsync(new Uri("/admin/proxy/status", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, isolated.StatusCode);
        using var managementBusiness = await administrator.Client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, managementBusiness.StatusCode);
        using var effective = await administrator.Client.GetAsync(new Uri("/admin/proxy/config/effective", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, effective.StatusCode);
        var json = await effective.Content.ReadAsStringAsync().ConfigureAwait(true);
        Assert.DoesNotContain(token, json, StringComparison.Ordinal);
        using var configuration = JsonDocument.Parse(json);
        Assert.True(configuration.RootElement.GetProperty("adminSecurity").GetProperty("requireAuthentication").GetBoolean());
        using var recent = await administrator.Client.GetAsync(new Uri("/admin/proxy/audit/recent?limit=20", UriKind.Relative)).ConfigureAwait(true);
        var events = await recent.Content.ReadFromJsonAsync<ProxyAdminAuditEventResponse[]>().ConfigureAwait(true);
        Assert.NotNull(events);
        Assert.Contains(events, static value => value.StatusCode == 401 && !value.Succeeded);
        Assert.Contains(events, static value => value.StatusCode == 403 && !value.Succeeded);
        Assert.Contains(events, static value => value.StatusCode == 200 && value.Succeeded);
    }

    [Theory]
    [InlineData("{\"format\":\"json\",\"format\":\"yaml\",\"text\":\"{}\"}")]
    [InlineData("{\"unknown\":true}")]
    [InlineData("null")]
    public async Task ApplicationRejectsAmbiguousOrUnknownControlPayloadsAndRecordsFailureAsync(string json)
    {
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort(), enrolledSite: true, administration: true, manualRoute: false).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var administrator = await CreateAdministratorAsync(proxy).ConfigureAwait(true);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var result = await administrator.Client.PostAsync(new Uri("/admin/proxy/config/normalize", UriKind.Relative), content).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        using var recent = await administrator.Client.GetAsync(new Uri("/admin/proxy/audit/recent?limit=20", UriKind.Relative)).ConfigureAwait(true);
        var events = await recent.Content.ReadFromJsonAsync<ProxyAdminAuditEventResponse[]>().ConfigureAwait(true);
        Assert.NotNull(events);
        Assert.Contains(events, static value => value.Path.EndsWith("ConfigurationNormalize", StringComparison.Ordinal) && value.StatusCode == 400 && !value.Succeeded);
    }

    [Fact]
    public async Task ExistingCacheConfigDiagnosticsAcmeMetricsAndBackupOperationsCrossThePrivateBoundaryAsync()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("business")).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, enrolledSite: true, administration: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var administrator = await CreateAdministratorAsync(proxy).ConfigureAwait(true);
        foreach (var path in new[] { "/admin/proxy/cache/status", "/admin/proxy/config/active", "/admin/proxy/diagnostics/recent", "/admin/proxy/backup/manifest" })
        {
            using var response = await administrator.Client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(true));
            Assert.NotEqual(JsonValueKind.Null, json.RootElement.ValueKind);
        }
        using var clear = await administrator.Client.PostAsync(new Uri("/admin/proxy/cache/clear", UriKind.Relative), null).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        using var acme = await administrator.Client.GetAsync(new Uri("/admin/proxy/acme/status", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, acme.StatusCode);
        using var metrics = await administrator.Client.GetAsync(new Uri("/admin/proxy/metrics", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, metrics.StatusCode);
        Assert.Contains("mdrava_requests_total", await metrics.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
        await AssertNormalizationLintAndRouteAsync(administrator.Client, proxy.Port).ConfigureAwait(true);
        using var validated = await administrator.Client.PostAsync(new Uri("/admin/proxy/config/validate", UriKind.Relative), null).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, validated.StatusCode);
    }

    private static async Task AssertNormalizationLintAndRouteAsync(HttpClient client, int port)
    {
        const string site = """
            {"name":"test","host":"app.test","listeners":[{"name":"http","address":"127.0.0.1","port":8081}],"pathPrefix":"/","upstreams":[{"name":"test","address":"127.0.0.1","port":8080}]}
            """;
        using var normalize = await client.PostAsJsonAsync(new Uri("/admin/proxy/config/normalize", UriKind.Relative), new ProxyConfigurationNormalizeSubmissionRequest("json", site)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, normalize.StatusCode);
        using var lint = await client.PostAsJsonAsync(new Uri("/admin/proxy/config/lint", UriKind.Relative), new ProxyConfigLintSubmissionRequest("json", site)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, lint.StatusCode);
        using var matched = await client.PostAsJsonAsync(new Uri("/admin/proxy/routes/match", UriKind.Relative), new ProxyRouteMatchDryRunRequest("http", "app.test", port, "GET", "/", "", null, "127.0.0.1", "http", "http1")).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, matched.StatusCode);
    }

    internal static async Task<DevelopmentSiteClient> CreateAdministratorAsync(TwoProcessProxy proxy)
    {
        var token = await File.ReadAllTextAsync(proxy.AdministratorTokenPath).ConfigureAwait(false);
        var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.ManagementPort, "admin.site.test", proxy.NodeCertificatePath);
        client.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
