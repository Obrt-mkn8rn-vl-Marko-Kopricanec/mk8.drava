using System.Net;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Contracts.Registration.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class RegisteredReplicaBalancingProcessTests
{
    [Fact]
    public async Task MultiplexedRequestsAvoidAnOccupiedReplicaAndDrainPreservesItsAcceptedStreamAsync()
    {
        var workload = new ReplicaWorkload();
        var first = await DevelopmentHttpUpstream.StartAsync(workload.Handler("first")).ConfigureAwait(true);
        await using var firstLifetime = first.ConfigureAwait(true);
        var second = await DevelopmentHttpUpstream.StartAsync(workload.Handler("second")).ConfigureAwait(true);
        await using var secondLifetime = second.ConfigureAwait(true);
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(first.Port, enrolledSite: true, manualRoute: false, dnsPort: dns.Port).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var registration = new DevelopmentRegistrationClient(proxy);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var firstIdentity = Identity();
        var secondIdentity = Identity();
        await registration.SubmitAsync(Register(firstIdentity, first.Port), deadline.Token).ConfigureAwait(true);
        await registration.SubmitAsync(Register(secondIdentity, second.Port), deadline.Token).ConfigureAwait(true);
        await WaitReadyAsync(registration, firstIdentity, deadline.Token).ConfigureAwait(true);
        await WaitReadyAsync(registration, secondIdentity, deadline.Token).ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        await VerifyHeldSelectionAndDrainAsync(client, registration, firstIdentity, secondIdentity, workload, deadline.Token).ConfigureAwait(true);
    }

    private static async Task VerifyHeldSelectionAndDrainAsync(DevelopmentSiteClient client, DevelopmentRegistrationClient registration,
        RegistrationIdentity first, RegistrationIdentity second, ReplicaWorkload workload, CancellationToken cancellationToken)
    {
        var held = ReadHeldResponseAsync(client.Client, workload, cancellationToken);
        try
        {
            await workload.PublicHead.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            var occupied = await workload.Occupied.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            var available = string.Equals(occupied, "first", StringComparison.Ordinal) ? "second" : "first";
            Assert.False(held.IsCompleted);
            await VerifyFollowingRequestsAsync(client.Client, available, cancellationToken).ConfigureAwait(true);
            var identity = string.Equals(occupied, "first", StringComparison.Ordinal) ? first : second;
            var drained = await registration.SubmitAsync(new RegistrationCommand { Identity = identity, Operation = RegistrationOperation.Drain }, cancellationToken).ConfigureAwait(true);
            Assert.Equal(RegistrationPhase.Draining, drained.Phase);
            await VerifyFollowingRequestsAsync(client.Client, available, cancellationToken).ConfigureAwait(true);
            Assert.False(held.IsCompleted);
            Assert.Equal(1, client.ConnectedSockets);
        }
        finally
        {
            workload.Release.TrySetResult();
            await held.ConfigureAwait(true);
        }
    }

    private static async Task ReadHeldResponseAsync(HttpClient client, ReplicaWorkload workload, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri("/held", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version20, response.Version);
        workload.PublicHead.TrySetResult();
        Assert.Equal("ok", await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
    }

    private static async Task VerifyFollowingRequestsAsync(HttpClient client, string expected, CancellationToken cancellationToken)
    {
        // Each unknown-length response must reach HTTP/2 END_STREAM, after private
        // exchange settlement, before selecting against the still-occupied replica again.
        for (var index = 0; index < 12; index++)
            Assert.Equal(expected, await ReadReplicaAsync(client, cancellationToken).ConfigureAwait(true));
    }

    private static async Task<string> ReadReplicaAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri("/short", UriKind.Relative), cancellationToken).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version20, response.Version);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
    }

    private static RegistrationIdentity Identity() => new()
    {
        SiteId = "development", NodeId = "local", OwnerId = "development", ServiceId = "svc", ContractId = "v1",
        InstanceId = Guid.NewGuid().ToString("N"), BootId = Guid.NewGuid().ToString("N"),
    };

    private static RegistrationCommand Register(RegistrationIdentity identity, int port) => new()
    {
        Identity = identity, Operation = RegistrationOperation.Register,
        Advertisement = new ServiceAdvertisement { DeploymentId = "development", Address = "127.0.0.1", Port = port, ReadinessPath = "/ready" },
    };

    private static async Task WaitReadyAsync(DevelopmentRegistrationClient client, RegistrationIdentity identity, CancellationToken cancellationToken)
    {
        while (true)
        {
            var status = await client.SubmitAsync(new RegistrationCommand { Identity = identity, Operation = RegistrationOperation.Status }, cancellationToken).ConfigureAwait(true);
            if (status.Phase == RegistrationPhase.Ready) return;
            await Task.Delay(100, cancellationToken).ConfigureAwait(true);
        }
    }

    private sealed class ReplicaWorkload
    {
        public TaskCompletionSource<string> Occupied { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PublicHead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RequestDelegate Handler(string replica) => async context =>
        {
            if (context.Request.Path == "/held")
            {
                context.Response.ContentLength = 2;
                await context.Response.WriteAsync("o", context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                Occupied.TrySetResult(replica);
                await Release.Task.WaitAsync(context.RequestAborted).ConfigureAwait(false);
                await context.Response.WriteAsync("k", context.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                await context.Response.StartAsync(context.RequestAborted).ConfigureAwait(false);
                await context.Response.WriteAsync(context.Request.Path == "/ready" ? "ready" : replica, context.RequestAborted).ConfigureAwait(false);
            }
        };
    }
}
