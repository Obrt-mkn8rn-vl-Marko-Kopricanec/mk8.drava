using System.Net;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class GatewayLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ColdGatewayStartsWithoutApplicationAndRecoversWhenItArrivesAsync(bool enrolled)
    {
        var backend = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("recovered", context.RequestAborted)).ConfigureAwait(true);
        await using var backendLifetime = backend.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(backend.Port, enrolledSite: enrolled, startApplication: false).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var live = await proxy.Client.GetAsync(new Uri("/_drava/live", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using var absent = await proxy.Client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, absent.StatusCode);
        Assert.False(File.Exists(proxy.GatewayPlanPath));
        await proxy.StartApplicationAsync().ConfigureAwait(true);
        await WaitForRecoveredAsync(proxy.Client).ConfigureAwait(true);
        if (enrolled)
        {
            Assert.True(File.Exists(proxy.GatewayPlanPath));
            // The manual route uses its existing host. Its HTTP endpoint proves recovery; TLS liveness proves the late certificate bind.
            using var tlsLive = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "probe.site.test");
            Assert.Equal("running", await tlsLive.Client.GetStringAsync(new Uri("/_drava/live", UriKind.Relative)).ConfigureAwait(true));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GatewaySurvivesApplicationRestartAndCachedGatewayRestartsWithoutApplicationAsync(bool enrolled)
    {
        var backend = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("recovered", context.RequestAborted)).ConfigureAwait(true);
        await using var backendLifetime = backend.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(backend.Port, enrolledSite: enrolled).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        await WaitForRecoveredAsync(proxy.Client).ConfigureAwait(true);
        var issued = enrolled ? await File.ReadAllBytesAsync(proxy.ApplicationPlanPath).ConfigureAwait(true) : null;
        await proxy.StopApplicationAsync().ConfigureAwait(true);
        using var live = await proxy.Client.GetAsync(new Uri("/_drava/live", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using var absent = await proxy.Client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, absent.StatusCode);
        await proxy.StartApplicationAsync().ConfigureAwait(true);
        await WaitForRecoveredAsync(proxy.Client).ConfigureAwait(true);
        if (enrolled) Assert.Equal(issued, await File.ReadAllBytesAsync(proxy.ApplicationPlanPath).ConfigureAwait(true));
        await proxy.StopApplicationAsync().ConfigureAwait(true);
        await proxy.RestartGatewayAsync().ConfigureAwait(true);
        if (enrolled)
        {
            using var tls = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "probe.site.test");
            Assert.Equal("running", await tls.Client.GetStringAsync(new Uri("/_drava/live", UriKind.Relative)).ConfigureAwait(true));
        }
        await proxy.StartApplicationAsync().ConfigureAwait(true);
        await WaitForRecoveredAsync(proxy.Client).ConfigureAwait(true);
    }

    private static async Task WaitForRecoveredAsync(HttpClient client)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            using var response = await client.GetAsync(new Uri("/", UriKind.Relative), timeout.Token).ConfigureAwait(true);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Assert.Equal("recovered", await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(true));
                return;
            }
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            await Task.Delay(50, timeout.Token).ConfigureAwait(true);
        }
    }
}
