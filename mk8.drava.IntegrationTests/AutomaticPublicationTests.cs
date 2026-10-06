using System.Net;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Contracts.Registration.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class AutomaticPublicationTests
{
    [Fact]
    public async Task RegisteredRandomPortServiceGetsARealTlsUrlWithoutAnyProxyFileAndDrainImmediatelyClosesAssignmentAsync()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync(context.Request.Path == "/ready" ? "ready" : "registered-response")).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, enrolledSite: true, manualRoute: false, dnsPort: dns.Port).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var registration = new DevelopmentRegistrationClient(proxy);
        var identity = Identity();
        var command = Register(identity, upstream.Port);
        var pending = await registration.SubmitAsync(command, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(RegistrationPhase.Checking, pending.Phase);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        await WaitForUnavailableRouteAsync(client.Client).ConfigureAwait(true);
        var ready = await WaitForPhaseAsync(registration, identity, RegistrationPhase.Ready).ConfigureAwait(true);
        Assert.Collection(ready.AssignedUrls, url => Assert.Equal($"https://svc.site.test:{proxy.TlsPort}/", url));
        using var routed = await client.Client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, routed.StatusCode);
        Assert.Equal("registered-response", await routed.Content.ReadAsStringAsync().ConfigureAwait(true));
        await proxy.WritePolicyAsync("{\"version\":2}").ConfigureAwait(true);
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        using var retained = await client.Client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
        var drained = await registration.SubmitAsync(new RegistrationCommand { Identity = identity, Operation = RegistrationOperation.Drain }, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(RegistrationPhase.Draining, drained.Phase);
        using var after = await client.Client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, after.StatusCode);
    }

    [Fact]
    public async Task WrongDnsAddressKeepsAHealthyEnrolledServicePendingAsync()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("ready")).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var dns = new DevelopmentDnsServer(IPAddress.Parse("192.0.2.123"));
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, enrolledSite: true, manualRoute: false, dnsPort: dns.Port).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var registration = new DevelopmentRegistrationClient(proxy);
        var identity = Identity();
        await registration.SubmitAsync(Register(identity, upstream.Port), CancellationToken.None).ConfigureAwait(true);
        var pending = await WaitForPhaseAsync(registration, identity, RegistrationPhase.DnsPending).ConfigureAwait(true);
        Assert.Empty(pending.AssignedUrls);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        using var response = await client.Client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static RegistrationIdentity Identity() => new() { SiteId = "development", NodeId = "local", OwnerId = "development", ServiceId = "svc", ContractId = "v1", InstanceId = Guid.NewGuid().ToString("N"), BootId = Guid.NewGuid().ToString("N") };
    private static RegistrationCommand Register(RegistrationIdentity identity, int port) => new() { Identity = identity, Operation = RegistrationOperation.Register,
        Advertisement = new ServiceAdvertisement { DeploymentId = "development", Address = "127.0.0.1", Port = port, ReadinessPath = "/ready" } };

    private static async Task<RegistrationStatus> WaitForPhaseAsync(DevelopmentRegistrationClient client, RegistrationIdentity identity, RegistrationPhase phase)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        RegistrationStatus status;
        do
        {
            await Task.Delay(200, deadline.Token).ConfigureAwait(false);
            status = await client.SubmitAsync(new RegistrationCommand { Identity = identity, Operation = RegistrationOperation.Status }, deadline.Token).ConfigureAwait(false);
        } while (status.Phase != phase);
        return status;
    }

    private static async Task WaitForUnavailableRouteAsync(HttpClient client)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        do
        {
            using var response = await client.GetAsync(new Uri("/", UriKind.Relative), deadline.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable) return;
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await Task.Delay(50, deadline.Token).ConfigureAwait(false);
        } while (true);
    }
}
