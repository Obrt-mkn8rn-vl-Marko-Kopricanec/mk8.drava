using System.Net;
using System.Net.Http.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Contracts.Registration.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class RegistryAdministrationTests
{
    [Fact]
    public async Task RegistryPagesAndExactBootRevocationAreAuthoritativeThroughThePrivateControlBoundaryAsync()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("registry-body")).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, enrolledSite: true, administration: true, manualRoute: false, dnsPort: dns.Port).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var administrator = await AdministrationProcessTests.CreateAdministratorAsync(proxy).ConfigureAwait(true);
        var first = await RegisterAsync(proxy, upstream.Port).ConfigureAwait(true);
        var second = await RegisterAsync(proxy, upstream.Port).ConfigureAwait(true);
        using var pageOne = await administrator.Client.GetAsync(new Uri("/admin/drava/registry?limit=1", UriKind.Relative)).ConfigureAwait(true);
        var one = await pageOne.Content.ReadFromJsonAsync<RegistryStateResponse>().ConfigureAwait(true);
        Assert.NotNull(one);
        Assert.Collection(one.Instances, static _ => { });
        Assert.NotEmpty(one.NextId);
        using var pageTwo = await administrator.Client.GetAsync(new Uri("/admin/drava/registry?limit=1&afterId=" + one.NextId, UriKind.Relative)).ConfigureAwait(true);
        var two = await pageTwo.Content.ReadFromJsonAsync<RegistryStateResponse>().ConfigureAwait(true);
        Assert.NotNull(two);
        Assert.Collection(two.Instances, static _ => { });
        Assert.Empty(two.NextId);
        Assert.NotEqual(one.Instances[0].Identity.InstanceId, two.Instances[0].Identity.InstanceId, StringComparer.Ordinal);
        using var revoked = await administrator.Client.PostAsJsonAsync(new Uri("/admin/drava/instances/revoke", UriKind.Relative), first).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        using var registration = new DevelopmentRegistrationClient(proxy);
        var oldBoot = await Assert.ThrowsAsync<RpcException>(() => registration.SubmitAsync(Command(first, upstream.Port), CancellationToken.None)).ConfigureAwait(true);
        Assert.Equal(StatusCode.InvalidArgument, oldBoot.StatusCode);
        var retired = await registration.SubmitAsync(new RegistrationCommand { Identity = first, Operation = RegistrationOperation.Status }, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(RegistrationPhase.Revoked, retired.Phase);
        await WaitReadyAsync(proxy, second).ConfigureAwait(true);
        await AssertNodeRetirementAndRestartAsync(proxy, administrator.Client, registration, second, upstream.Port).ConfigureAwait(true);
    }

    private static async Task AssertNodeRetirementAndRestartAsync(TwoProcessProxy proxy, HttpClient administrator, DevelopmentRegistrationClient registration, RegistrationIdentity second, int port)
    {
        using var revokedNode = await administrator.PostAsJsonAsync(new Uri("/admin/drava/nodes/revoke", UriKind.Relative), new NodeRevokeRequest { NodeId = "local" }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, revokedNode.StatusCode);
        var oldNode = await Assert.ThrowsAsync<RpcException>(() => registration.SubmitAsync(Command(second, port), CancellationToken.None)).ConfigureAwait(true);
        Assert.Equal(StatusCode.Unauthenticated, oldNode.StatusCode);
        using var empty = await administrator.GetAsync(new Uri("/admin/drava/registry", UriKind.Relative)).ConfigureAwait(true);
        var view = await empty.Content.ReadFromJsonAsync<RegistryStateResponse>().ConfigureAwait(true);
        Assert.NotNull(view);
        Assert.Collection(view.Instances, AssertRetired, AssertRetired);
        Assert.Equal(2, view.TombstoneCount);
        using var nodes = await administrator.GetAsync(new Uri("/admin/drava/registry?kind=nodes", UriKind.Relative)).ConfigureAwait(true);
        var nodeView = await nodes.Content.ReadFromJsonAsync<RegistryStateResponse>().ConfigureAwait(true);
        Assert.NotNull(nodeView);
        Assert.Collection(nodeView.Nodes, static node => Assert.True(node.Revoked));
        await proxy.RestartAsync().ConfigureAwait(true);
        using var restartedAdministrator = await AdministrationProcessTests.CreateAdministratorAsync(proxy).ConfigureAwait(true);
        using var restored = await restartedAdministrator.Client.GetAsync(new Uri("/admin/drava/registry", UriKind.Relative)).ConfigureAwait(true);
        var restoredView = await restored.Content.ReadFromJsonAsync<RegistryStateResponse>().ConfigureAwait(true);
        Assert.NotNull(restoredView);
        Assert.Collection(restoredView.Instances, AssertRetired, AssertRetired);
    }

    internal static async Task<RegistrationIdentity> RegisterAsync(TwoProcessProxy proxy, int port)
    {
        var identity = new RegistrationIdentity { SiteId = "development", NodeId = "local", OwnerId = "development", ServiceId = "svc", ContractId = "v1", InstanceId = Guid.NewGuid().ToString("N"), BootId = Guid.NewGuid().ToString("N") };
        using var registration = new DevelopmentRegistrationClient(proxy);
        await registration.SubmitAsync(Command(identity, port), CancellationToken.None).ConfigureAwait(false);
        await WaitReadyAsync(proxy, identity).ConfigureAwait(false);
        return identity;
    }

    internal static async Task WaitReadyAsync(TwoProcessProxy proxy, RegistrationIdentity identity)
    {
        using var registration = new DevelopmentRegistrationClient(proxy);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        while (true)
        {
            var status = await registration.SubmitAsync(new RegistrationCommand { Identity = identity, Operation = RegistrationOperation.Status }, deadline.Token).ConfigureAwait(false);
            if (status.Phase == RegistrationPhase.Ready) return;
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }
    }

    private static RegistrationCommand Command(RegistrationIdentity identity, int port) => new()
    {
        Identity = identity, Operation = RegistrationOperation.Register,
        Advertisement = new ServiceAdvertisement { DeploymentId = "development", Address = "127.0.0.1", Port = port, ReadinessPath = "/ready" },
    };

    private static void AssertRetired(RegisteredInstanceResponse instance)
    {
        Assert.True(instance.Draining);
        Assert.True(instance.Revoked);
        Assert.False(instance.LeaseValid);
    }
}
