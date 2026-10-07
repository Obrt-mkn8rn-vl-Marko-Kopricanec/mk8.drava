using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Registration;
using Mk8.Drava.Transport.Discovery;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Relay;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class NodeRelayProcessTests
{
    [Theory]
    [InlineData(false, 90)]
    [InlineData(true, 90)]
    [InlineData(false, 15)]
    public async Task LogicalRemoteLoopbackSdkUsesTheAgentAndNeverFallsBackToControllerLoopbackAsync(bool backendTls, int mappingLeaseSeconds)
    {
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort(), enrolledSite: true, manualRoute: false, dnsPort: dns.Port, relayNode: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var agent = await DevelopmentNodeAgent.StartAsync(proxy, new RelayLimits { StreamWindowFrames = 1, MaximumBytesPerDirection = 8 * 1024 * 1024, MaximumDurationSeconds = 20 }, mappingLeaseSeconds).ConfigureAwait(true);
        await using var agentLifetime = agent.ConfigureAwait(true);
        Assert.Equal("remote", agent.Descriptor.NodeId);
        Assert.False(IPAddress.IsLoopback(IPAddress.Parse(agent.Descriptor.RelayAddress)));
        Assert.Empty(Directory.EnumerateFiles(agent.StateDirectory, "*.db"));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=backend.site.invalid", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        if (backendTls) await proxy.WritePolicyAsync("{\"global\":{\"upstreamTls\":{\"validateCertificate\":false,\"sniHost\":\"backend.site.invalid\"}}}").ConfigureAwait(true);
        var names = new ConcurrentQueue<string?>();
        var backend = await DevelopmentHttpUpstream.StartAsync(RespondAsync, backendTls ? certificate : null, names.Enqueue, Options(proxy)).ConfigureAwait(true);
        await using var backendLifetime = backend.ConfigureAwait(true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(mappingLeaseSeconds < 30 ? 90 : 45));
        while (backend.RegistrationState.Status?.Phase != RegistrationPhase.Ready) await Task.Delay(100, timeout.Token).ConfigureAwait(true);
        Assert.Equal("remote", backend.RegistrationState.Status!.Identity.NodeId);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        if (mappingLeaseSeconds < 30) await Task.Delay(TimeSpan.FromSeconds(18), timeout.Token).ConfigureAwait(true);
        await AssertTrafficAsync(client.Client, timeout.Token).ConfigureAwait(true);
        if (backendTls) Assert.Contains("backend.site.invalid", names, StringComparer.Ordinal);
        await agent.DisposeAsync().ConfigureAwait(true);
        await AssertNoFallbackAsync(proxy, client.Client, backend.Port, backendTls, certificate, timeout.Token).ConfigureAwait(true);
    }

    [Fact]
    public async Task NodeAgentRestartRenewsTheDescriptorAndMappingWithoutRestartingGatewayOrServiceAsync()
    {
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort(), enrolledSite: true, manualRoute: false, dnsPort: dns.Port, relayNode: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var agent = await DevelopmentNodeAgent.StartAsync(proxy).ConfigureAwait(true);
        await using var agentLifetime = agent.ConfigureAwait(true);
        var backend = await DevelopmentHttpUpstream.StartAsync(RespondAsync, registration: Options(proxy)).ConfigureAwait(true);
        await using var backendLifetime = backend.ConfigureAwait(true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        while (backend.RegistrationState.Status?.Phase != RegistrationPhase.Ready) await Task.Delay(100, timeout.Token).ConfigureAwait(true);
        var identity = backend.RegistrationState.Status!.Identity;
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        await AssertTrafficAsync(client.Client, timeout.Token).ConfigureAwait(true);
        await agent.DisposeAsync().ConfigureAwait(true);
        var restarted = await DevelopmentNodeAgent.StartAsync(proxy).ConfigureAwait(true);
        await using var restartedLifetime = restarted.ConfigureAwait(true);
        Assert.NotEqual(agent.Descriptor.AgentBootId, restarted.Descriptor.AgentBootId, StringComparer.Ordinal);
        Assert.Equal(agent.Descriptor.CertificateFingerprint, restarted.Descriptor.CertificateFingerprint);
        while (true)
        {
            using var response = await client.Client.GetAsync(new Uri("/", UriKind.Relative), timeout.Token).ConfigureAwait(true);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Assert.Equal(new string('r', 300 * 1024), await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(true));
                break;
            }
            Assert.True(response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout);
            await Task.Delay(250, timeout.Token).ConfigureAwait(true);
        }
        while (backend.RegistrationState.Status?.Phase != RegistrationPhase.Ready) await Task.Delay(100, timeout.Token).ConfigureAwait(true);
        Assert.Equal(identity.InstanceId, backend.RegistrationState.Status!.Identity.InstanceId);
        Assert.Equal(identity.BootId, backend.RegistrationState.Status.Identity.BootId);
        await AssertTrafficAsync(client.Client, timeout.Token).ConfigureAwait(true);
    }

    private static async Task RespondAsync(HttpContext context)
    {
        if (string.Equals(context.Request.Path.Value, "/ready", StringComparison.Ordinal)) { await context.Response.WriteAsync("ready", context.RequestAborted).ConfigureAwait(false); return; }
        if (HttpMethods.IsPost(context.Request.Method))
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted).ConfigureAwait(false);
            await context.Response.WriteAsync(Convert.ToHexString(SHA256.HashData(body.ToArray())), context.RequestAborted).ConfigureAwait(false);
        }
        else await context.Response.WriteAsync(new string('r', 300 * 1024), context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task AssertTrafficAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri("/", UriKind.Relative), cancellationToken).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new string('r', 300 * 1024), await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
        var payload = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        using var content = new ByteArrayContent(payload);
        using var posted = await client.PostAsync(new Uri("/", UriKind.Relative), content, cancellationToken).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)), await posted.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
    }

    private static async Task AssertNoFallbackAsync(TwoProcessProxy proxy, HttpClient client, int backendPort, bool tls, X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler { UseProxy = false, SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, peer, _, _) => peer is not null && string.Equals(peer.GetCertHashString(HashAlgorithmName.SHA256), certificate.GetCertHashString(HashAlgorithmName.SHA256), StringComparison.Ordinal),
        } };
        using var direct = new HttpClient(handler, disposeHandler: false);
        using var stillRunning = await direct.GetAsync(new Uri($"{(tls ? "https" : "http")}://127.0.0.1:{backendPort}/ready"), cancellationToken).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, stillRunning.StatusCode);
        using var stopped = await client.GetAsync(new Uri("/", UriKind.Relative), cancellationToken).ConfigureAwait(true);
        Assert.True(stopped.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout);
        Assert.Equal("remote", proxy.EnrolledNodeId);
    }

    [Fact]
    public async Task EnrolledNodeCredentialCannotOpenTheControllerRelayListenerAsync()
    {
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort(), enrolledSite: true, manualRoute: false, relayNode: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var agent = await DevelopmentNodeAgent.StartAsync(proxy).ConfigureAwait(true);
        await using var agentLifetime = agent.ConfigureAwait(true);
        using var root = X509CertificateLoader.LoadCertificateFromFile(proxy.RootCertificatePath);
        using var node = X509CertificateLoader.LoadPkcs12FromFile(proxy.NodeCertificatePath, password: null, X509KeyStorageFlags.EphemeralKeySet);
        using var handler = new SocketsHttpHandler { UseProxy = false, SslOptions = new SslClientAuthenticationOptions
        {
            ClientCertificates = new X509CertificateCollection { node },
            RemoteCertificateValidationCallback = (_, peer, _, _) => peer is not null && ValidatePeer(peer, root, agent.Descriptor),
        } };
        using var channel = GrpcChannel.ForAddress($"https://{agent.Descriptor.RelayAddress}:{agent.Descriptor.RelayPort}", new GrpcChannelOptions { HttpHandler = handler });
        using var call = new Mk8.Drava.Transport.Protocol.V1.NodeRelay.NodeRelayClient(channel).Relay(deadline: DateTime.UtcNow.AddSeconds(5));
        await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
        {
            await call.RequestStream.WriteAsync(new RelayFrame { Open = new RelayOpen { Version = 1 } }).ConfigureAwait(true);
            await call.ResponseStream.MoveNext(CancellationToken.None).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private static bool ValidatePeer(X509Certificate certificate, X509Certificate2 root, NodeAgentDescriptor descriptor)
    {
        using var leaf = new X509Certificate2(certificate);
        return NodeCertificateTrust.Validate(leaf, root, descriptor.RelayAddress, descriptor.CertificateFingerprint, TimeProvider.System);
    }

    private static DravaRegistrationOptions Options(TwoProcessProxy proxy)
    {
        using var root = X509CertificateLoader.LoadCertificateFromFile(proxy.RootCertificatePath);
        return new DravaRegistrationOptions
        {
            Site = new RegistrationSiteTrust { SiteId = "development", Domain = "site.test", RootFingerprint = root.GetCertHashString(HashAlgorithmName.SHA256), RootCertificatePath = proxy.RootCertificatePath, NodeCertificatePath = proxy.NodeCertificatePath },
            NodeId = proxy.EnrolledNodeId, OwnerId = "development", ServiceId = "svc", ReadinessPath = "/ready", MulticastDiscovery = false,
            GatewaySeeds = [new DiscoveryCandidate("127.0.0.1", proxy.RegistrationPort)],
        };
    }
}
