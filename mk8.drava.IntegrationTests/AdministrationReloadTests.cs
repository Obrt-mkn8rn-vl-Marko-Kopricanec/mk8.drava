using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Contracts.Registration.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class AdministrationReloadTests
{
    [Fact]
    public async Task ManualReloadRetainsRegisteredRoutesAndRejectsListenerChangesWithoutLosingTheAcceptedSnapshotAsync()
    {
        var old = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("old-body")).ConfigureAwait(true);
        await using var oldLifetime = old.ConfigureAwait(true);
        var next = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("new-body")).ConfigureAwait(true);
        await using var nextLifetime = next.ConfigureAwait(true);
        var automatic = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("automatic-body")).ConfigureAwait(true);
        await using var automaticLifetime = automatic.ConfigureAwait(true);
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(old.Port, enrolledSite: true, administration: true, dnsPort: dns.Port).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var administrator = await AdministrationProcessTests.CreateAdministratorAsync(proxy).ConfigureAwait(true);
        using var registration = new DevelopmentRegistrationClient(proxy);
        var identity = new RegistrationIdentity { SiteId = "development", NodeId = "local", OwnerId = "development", ServiceId = "svc", ContractId = "v1", InstanceId = Guid.NewGuid().ToString("N"), BootId = Guid.NewGuid().ToString("N") };
        await registration.SubmitAsync(new RegistrationCommand { Identity = identity, Operation = RegistrationOperation.Register,
            Advertisement = new ServiceAdvertisement { DeploymentId = "development", Address = "127.0.0.1", Port = automatic.Port, ReadinessPath = "/ready" } }, CancellationToken.None).ConfigureAwait(true);
        await WaitForReadyAsync(registration, identity).ConfigureAwait(true);
        await AssertBodyAsync(proxy.Client, "old-body").ConfigureAwait(true);
        await proxy.WriteManualRouteAsync(next.Port).ConfigureAwait(true);
        using var reloaded = await administrator.Client.PostAsync(new Uri("/admin/proxy/config/reload", UriKind.Relative), null).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, reloaded.StatusCode);
        var result = await reloaded.Content.ReadFromJsonAsync<ProxyConfigurationReloadResponse>().ConfigureAwait(true);
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        await AssertBodyAsync(proxy.Client, "new-body").ConfigureAwait(true);
        await WaitForReadyAsync(registration, identity).ConfigureAwait(true);
        using var automaticClient = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        await AssertBodyAsync(automaticClient.Client, "automatic-body").ConfigureAwait(true);
        await proxy.WriteManualRouteAsync(old.Port, TwoProcessProxy.UnusedPort()).ConfigureAwait(true);
        using var rejected = await administrator.Client.PostAsync(new Uri("/admin/proxy/config/reload", UriKind.Relative), null).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await AssertBodyAsync(proxy.Client, "new-body").ConfigureAwait(true);
    }

    private static async Task AssertBodyAsync(HttpClient client, string expected)
    {
        using var response = await client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private static async Task WaitForReadyAsync(DevelopmentRegistrationClient client, RegistrationIdentity identity)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        while (true)
        {
            var status = await client.SubmitAsync(new RegistrationCommand { Identity = identity, Operation = RegistrationOperation.Status }, deadline.Token).ConfigureAwait(false);
            if (status.Phase == RegistrationPhase.Ready) return;
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }
    }
}
