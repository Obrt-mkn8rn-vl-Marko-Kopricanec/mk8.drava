using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Registration;
using Mk8.Drava.Transport.Discovery;
using Xunit;
using Xunit.Abstractions;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class RegisteredTransportCostProcessTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRegisteredUploadsRecordLocalAndRelayCostWithoutDuplicateOriginWritesAsync(bool relay)
    {
        var workload = new RegisteredUploadWorkload();
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort(), enrolledSite: true,
            manualRoute: false, dnsPort: dns.Port, relayNode: relay).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        if (relay)
        {
            var agent = await DevelopmentNodeAgent.StartAsync(proxy, new RelayLimits
            {
                MaximumConcurrentConnections = 8, MaximumBytesPerDirection = 64 * 1024 * 1024,
                MaximumDurationSeconds = 60, StreamWindowFrames = 1,
            }).ConfigureAwait(true);
            await using var agentLifetime = agent.ConfigureAwait(true);
            Assert.False(IPAddress.IsLoopback(IPAddress.Parse(agent.Descriptor.RelayAddress)));
            await RunWorkloadAsync(proxy, workload, agent, output).ConfigureAwait(true);
        }
        else
        {
            await RunWorkloadAsync(proxy, workload, agent: null, output).ConfigureAwait(true);
        }
    }

    private static async Task RunWorkloadAsync(TwoProcessProxy proxy, RegisteredUploadWorkload workload, DevelopmentNodeAgent? agent, ITestOutputHelper output)
    {
        var backend = await DevelopmentHttpUpstream.StartAsync(workload.HandleAsync, registration: await OptionsAsync(proxy).ConfigureAwait(true)).ConfigureAwait(true);
        await using var backendLifetime = backend.ConfigureAwait(true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        while (backend.RegistrationState.Status?.Phase != RegistrationPhase.Ready)
            await Task.Delay(100, deadline.Token).ConfigureAwait(true);
        Assert.Equal(agent is not null ? "remote" : "local", backend.RegistrationState.Status!.Identity.NodeId, StringComparer.Ordinal);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        try { await workload.RunAsync(client.Client, deadline.Token).ConfigureAwait(true); }
        finally { workload.WriteEvidence(output, agent is not null, proxy.ReadMemory(), agent?.ReadMemory(), client.ConnectedSockets); }
        Assert.Equal(1, client.ConnectedSockets);
        if (agent is not null)
        {
            await agent.DisposeAsync().ConfigureAwait(true);
            await AssertNoFallbackAsync(backend.Port, client.Client, deadline.Token).ConfigureAwait(true);
        }
    }

    private static async Task<DravaRegistrationOptions> OptionsAsync(TwoProcessProxy proxy)
    {
        using var root = X509CertificateLoader.LoadCertificateFromFile(proxy.RootCertificatePath);
        return new DravaRegistrationOptions
        {
            NodeAgentToken = proxy.NodeAgentToken, NodeId = proxy.EnrolledNodeId, OwnerId = "development", ServiceId = "svc",
            ReadinessPath = "/ready", MulticastDiscovery = false, GatewaySeeds = [new DiscoveryCandidate("127.0.0.1", proxy.RegistrationPort)],
            Site = new RegistrationSiteTrust
            {
                SiteId = "development", Domain = "site.test", RootFingerprint = root.GetCertHashString(HashAlgorithmName.SHA256),
                RootCertificate = new SecretBytes(root.RawData),
                NodeCertificate = new SecretBytes(await File.ReadAllBytesAsync(proxy.NodeCertificatePath, CancellationToken.None).ConfigureAwait(false)),
                NodeAgentDescriptorPath = proxy.NodeAgentDescriptorPath,
            },
        };
    }

    private static async Task AssertNoFallbackAsync(int backendPort, HttpClient publicClient, CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler { UseProxy = false };
        using var direct = new HttpClient(handler);
        using var ready = await direct.GetAsync(new UriBuilder("http", IPAddress.Loopback.ToString(), backendPort, "/ready").Uri, cancellationToken).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using var refused = await publicClient.GetAsync(new Uri("/ready", UriKind.Relative), cancellationToken).ConfigureAwait(true);
        Assert.True(refused.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout);
    }
}
