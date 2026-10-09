using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Contracts.Registration.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class AutomaticRouteControlsTests
{
    private static readonly string[] RemovedResponseHeaders = ["X-Remove", "Content-Type"];
    [Theory]
    [InlineData("proxy")]
    [InlineData("redirect")]
    [InlineData("staticResponse")]
    public async Task ConfiguredRouteHasTheExactAssignedUrlAndNativeActionAndStillClosesOnDrainAsync(string action)
    {
        var businessRequests = 0;
        var upstream = await DevelopmentHttpUpstream.StartAsync(context =>
        {
            context.Response.ContentType = "text/plain";
            context.Response.Headers["X-Remove"] = "origin";
            if (context.Request.Path != "/ready") Interlocked.Increment(ref businessRequests);
            return context.Response.WriteAsync("upstream:" + context.Request.Path + context.Request.QueryString);
        }).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, enrolledSite: true, administration: true, manualRoute: false, dnsPort: dns.Port, lifecycleSettings: Settings()).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var administrator = await AdministrationProcessTests.CreateAdministratorAsync(proxy).ConfigureAwait(true);
        var identity = await RegistryAdministrationTests.RegisterAsync(proxy, upstream.Port).ConfigureAwait(true);
        var accepted = await ApplyPolicyAsync(administrator.Client, "edge.site.test", action).ConfigureAwait(true);
        Assert.Collection(accepted.Services, service =>
        {
            Assert.Equal("edge.site.test", service.Host); Assert.Equal("/api/", service.PathPrefix);
            Assert.Equal(action, service.Action, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("service-profile", service.Provenance["host"]); Assert.Equal("route", service.Provenance["pathPrefix"]);
        });
        using var registration = new DevelopmentRegistrationClient(proxy);
        var ready = await WaitAsync(registration, identity, status => status.Phase == RegistrationPhase.Ready && status.AssignedUrls.Contains($"https://edge.site.test:{proxy.TlsPort}/api/", StringComparer.Ordinal)).ConfigureAwait(true);
        Assert.Collection(ready.AssignedUrls, url => Assert.Equal($"https://edge.site.test:{proxy.TlsPort}/api/", url));
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "edge.site.test");
        await AssertResponseAsync(client.Client, action).ConfigureAwait(true);
        Assert.Equal(string.Equals(action, "proxy", StringComparison.Ordinal) ? 1 : 0, Volatile.Read(ref businessRequests));
        await AssertDryRunAsync(administrator.Client, proxy.TlsPort, action).ConfigureAwait(true);
        using var outside = await client.Client.GetAsync(new Uri("/outside", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
        using var previousHost = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        using var previous = await previousHost.Client.GetAsync(new Uri("/api/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, previous.StatusCode);
        var drained = await registration.SubmitAsync(new RegistrationCommand { Identity = identity, Operation = RegistrationOperation.Drain }, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(RegistrationPhase.Draining, drained.Phase);
        using var after = await client.Client.GetAsync(new Uri("/api/echo", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, after.StatusCode);
    }

    [Theory]
    [InlineData("external.example")]
    [InlineData("deep.edge.site.test")]
    [InlineData("site.test")]
    public async Task HealthyDnsVerifiedRouteWithoutCertificateCoverageStaysPendingAsync(string host)
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("ready")).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, enrolledSite: true, administration: true, manualRoute: false, dnsPort: dns.Port, lifecycleSettings: Settings()).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var administrator = await AdministrationProcessTests.CreateAdministratorAsync(proxy).ConfigureAwait(true);
        var identity = await RegistryAdministrationTests.RegisterAsync(proxy, upstream.Port).ConfigureAwait(true);
        await ApplyPolicyAsync(administrator.Client, host, "proxy").ConfigureAwait(true);
        using var registration = new DevelopmentRegistrationClient(proxy);
        var pending = await WaitAsync(registration, identity, static status => status.Phase == RegistrationPhase.CertificatePending).ConfigureAwait(true);
        Assert.Empty(pending.AssignedUrls);
        Assert.Contains(host, pending.Reason, StringComparison.Ordinal);
        using var publicHttp = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{proxy.Port}"), Timeout = TimeSpan.FromSeconds(10) };
        publicHttp.DefaultRequestHeaders.Host = host;
        using var response = await publicHttp.GetAsync(new Uri("/api/echo", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static DevelopmentLifecycleSettings Settings() => new() { Registration = new RegistrationSettings {
        ReadinessIntervalMilliseconds = 250, ReadinessTimeoutMilliseconds = 1000, ReadinessValiditySeconds = 30,
        ReadinessSuccesses = 1, ReadinessFailures = 1, ReconcileIntervalMilliseconds = 100, MaximumConcurrentProbes = 2 } };

    private static async Task<PolicyStateResponse> ApplyPolicyAsync(HttpClient client, string host, string action)
    {
        using var read = await client.GetAsync(new Uri("/admin/drava/policy", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var initial = await read.Content.ReadFromJsonAsync<PolicyStateResponse>().ConfigureAwait(false);
        Assert.NotNull(initial);
        var policy = JsonSerializer.SerializeToElement(new { site = new { headers = new { setResponseHeaders = new[] { new { name = "X-Route", value = "configured" } }, removeResponseHeaders = RemovedResponseHeaders } },
            services = new[] { new { serviceId = "svc", profile = new { host },
            route = new { pathPrefix = "/api/", action, rewrite = new { stripPrefix = "/api" },
                redirect = new { targetPath = "/landing", statusCode = 307 }, staticResponse = new { statusCode = 202, body = "configured-static" } } } } });
        using var update = await client.PostAsJsonAsync(new Uri("/admin/drava/policy", UriKind.Relative), new PolicyUpdateRequest { ExpectedRevision = initial.AcceptedRevision, Policy = policy }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        return await update.Content.ReadFromJsonAsync<PolicyStateResponse>().ConfigureAwait(false) ?? throw new InvalidDataException("Applied policy is missing.");
    }

    private static async Task AssertResponseAsync(HttpClient client, string action)
    {
        using var response = await client.GetAsync(new Uri("/api/echo?x=1", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(action switch { "proxy" => HttpStatusCode.OK, "redirect" => HttpStatusCode.TemporaryRedirect, _ => HttpStatusCode.Accepted }, response.StatusCode);
        if (string.Equals(action, "redirect", StringComparison.Ordinal)) Assert.Equal("/landing?x=1", response.Headers.Location?.OriginalString);
        else Assert.Equal(string.Equals(action, "proxy", StringComparison.Ordinal) ? "upstream:/echo?x=1" : "configured-static", await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Collection(response.Headers.GetValues("X-Route"), static value => Assert.Equal("configured", value));
        Assert.False(response.Headers.Contains("X-Remove"));
        Assert.Null(response.Content.Headers.ContentType);
    }

    private static async Task AssertDryRunAsync(HttpClient client, int port, string action)
    {
        using var response = await client.PostAsJsonAsync(new Uri("/admin/proxy/routes/match", UriKind.Relative), new ProxyRouteMatchDryRunRequest("https", "edge.site.test", port, "GET", "/api/echo", "x=1", null, "127.0.0.1", "https", "http2")).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RouteMatchDryRunResponse>().ConfigureAwait(false);
        Assert.NotNull(result); Assert.True(result.Succeeded); Assert.NotNull(result.Route);
        Assert.NotNull(result.Listener); Assert.Equal("Http1AndHttp2", result.Listener.Protocols);
        Assert.Equal("edge.site.test", result.Route.Host); Assert.Equal("/api/", result.Route.PathPrefix);
        Assert.Equal(string.Equals(action, "proxy", StringComparison.Ordinal), result.WouldProxy);
    }

    private static async Task<RegistrationStatus> WaitAsync(DevelopmentRegistrationClient client, RegistrationIdentity identity, Func<RegistrationStatus, bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        while (true)
        {
            var status = await client.SubmitAsync(new RegistrationCommand { Identity = identity, Operation = RegistrationOperation.Status }, deadline.Token).ConfigureAwait(false);
            if (condition(status)) return status;
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }
    }
}
