using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Configuration;
using Mk8.Drava.Gateway.Hosting;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class PublicServingChainProcessTests
{
    [Fact]
    public async Task ColdTlsRejectionUsesFrameworkCleanupWhilePlainLivenessRemainsAvailableAsync()
    {
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort(), enrolledSite: true, startApplication: false).ConfigureAwait(true);
        try
        {
            using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "probe.site.test");
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                using var response = await client.Client.GetAsync(new Uri("/_drava/live", UriKind.Relative)).ConfigureAwait(false);
            }).ConfigureAwait(true);
            using var liveness = await proxy.Client.GetAsync(new Uri("/_drava/live", UriKind.Relative)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        }
        finally { await proxy.DisposeAsync().ConfigureAwait(true); }
        Assert.DoesNotContain("Unhandled exception while processing", proxy.GatewayLog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActualGatewaySendsPublicIntermediateAndRetainsPrivateListenerTrustAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var (gateway, bootstrapPath) = await PrepareGatewayAsync(fixture, publicMaterial).ConfigureAwait(true);
        var process = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.Gateway"), bootstrapPath);
        await using var lifetime = process.ConfigureAwait(true);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await WaitForListenerAsync(process, gateway.HttpsPort, deadline.Token).ConfigureAwait(true);
            var serving = await ReadCertificateAsync(gateway.HttpsPort, "svc.site.test", publicMaterial.Root, clientCertificate: null, requireIntermediate: true, deadline.Token).ConfigureAwait(true);
            Assert.Equal(publicMaterial.Leaf.GetCertHashString(HashAlgorithmName.SHA256), serving);
            using var privateRoot = fixture.Authority.PublicCertificate;
            using var node = fixture.Authority.IssueNode("node", ["127.0.0.1"]);
            var registration = await ReadCertificateAsync(gateway.RegistrationPort, "register.site.test", privateRoot, node, requireIntermediate: false, deadline.Token).ConfigureAwait(true);
            var management = await ReadCertificateAsync(gateway.ManagementPort, "admin.site.test", privateRoot, node, requireIntermediate: false, deadline.Token).ConfigureAwait(true);
            Assert.NotEqual(serving, registration, StringComparer.Ordinal);
            Assert.Equal(registration, management);
        }
        finally
        {
            await process.DisposeAsync().ConfigureAwait(true);
            var evidence = Path.Combine(TwoProcessProxy.FindRoot(), "artifacts", "public-serving-chain-tests", Path.GetFileName(fixture.Application.StateDirectory));
            Directory.CreateDirectory(evidence);
            await File.WriteAllTextAsync(Path.Combine(evidence, "gateway.log"), process.CapturedLog).ConfigureAwait(true);
        }
    }

    private static async Task<(GatewayBootstrap Gateway, string BootstrapPath)> PrepareGatewayAsync(DevelopmentServingPlanFixture fixture, DevelopmentPublicServingCertificate publicMaterial)
    {
        var trust = new ServingTrustSettings { Mode = "pinned", RootFingerprint = publicMaterial.Root.GetCertHashString(HashAlgorithmName.SHA256) };
        var publicPath = Path.Combine(fixture.Application.StateDirectory, "public.pfx");
        await Mk8.Drava.Application.DAL.Acme.PrivateCertificateFile.WriteNewAsync(publicPath, publicMaterial.Pfx, CancellationToken.None).ConfigureAwait(true);
        var gatewayDirectory = Path.Combine(fixture.Application.StateDirectory, "gateway");
        var token = Path.Combine(fixture.Application.StateDirectory, "gateway.token");
        await File.WriteAllTextAsync(token, Convert.ToHexString(RandomNumberGenerator.GetBytes(32))).ConfigureAwait(true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(token, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var ipc = OperatingSystem.IsWindows() ? new IpcEndpoint { NamedPipeName = "drava_" + Guid.NewGuid().ToString("N"), IdentityTokenPath = token }
            : new IpcEndpoint { UnixSocketPath = Path.Combine(fixture.Application.StateDirectory, "absent.sock"), IdentityTokenPath = token };
        var gateway = fixture.Gateway with
        {
            StateDirectory = gatewayDirectory, Application = ipc, ServingTrust = trust, DiscoveryEnabled = false,
            HttpPort = TwoProcessProxy.UnusedPort(), HttpsPort = TwoProcessProxy.UnusedPort(),
            RegistrationPort = TwoProcessProxy.UnusedPort(), ManagementPort = TwoProcessProxy.UnusedPort(),
        };
        var application = fixture.Application with
        {
            HttpPort = gateway.HttpPort, HttpsPort = gateway.HttpsPort, ManagementPort = gateway.ManagementPort,
            Controller = fixture.Application.Controller! with { RegistrationPort = gateway.RegistrationPort, ServingTrust = trust, ServingCertificatePath = publicPath },
        };
        using var plans = await ServingPlanState.OpenAsync(application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        using (var cache = new GatewayPlanCache(gatewayDirectory))
            await cache.WriteAsync(plans.Read("local"), CancellationToken.None).ConfigureAwait(true);
        var bootstrapPath = Path.Combine(fixture.Application.StateDirectory, "gateway.json");
        await File.WriteAllTextAsync(bootstrapPath, BootstrapFile.Serialize(gateway)).ConfigureAwait(true);
        return (gateway, bootstrapPath);
    }

    private static async Task WaitForListenerAsync(DevelopmentProcess process, int port, CancellationToken cancellationToken)
    {
        while (true)
        {
            process.ThrowIfExited();
            using var connection = new TcpClient();
            try { await connection.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false); return; }
            catch (SocketException) { }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> ReadCertificateAsync(int port, string host, X509Certificate2 root, X509Certificate2? clientCertificate, bool requireIntermediate, CancellationToken cancellationToken)
    {
        using var connection = new TcpClient();
        await connection.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
        string? fingerprint = null;
        var receivedIntermediate = false;
        using var tls = new SslStream(connection.GetStream(), leaveInnerStreamOpen: false, (_, certificate, receivedChain, errors) =>
        {
            if (certificate is null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != SslPolicyErrors.None) return false;
            using var leaf = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            var withoutIntermediate = chain.Build(leaf);
            if (receivedChain is not null)
                foreach (var element in receivedChain.ChainElements)
                    if (!element.Certificate.RawData.AsSpan().SequenceEqual(leaf.RawData)) chain.ChainPolicy.ExtraStore.Add(element.Certificate);
            receivedIntermediate = !withoutIntermediate && chain.ChainPolicy.ExtraStore.Count > 0;
            if (!chain.Build(leaf)) return false;
            fingerprint = leaf.GetCertHashString(HashAlgorithmName.SHA256);
            return true;
        });
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            ClientCertificates = clientCertificate is null ? null : new X509CertificateCollection { clientCertificate },
            ApplicationProtocols = [clientCertificate is null ? SslApplicationProtocol.Http11 : SslApplicationProtocol.Http2],
        }, cancellationToken).ConfigureAwait(false);
        if (clientCertificate is null)
        {
            await tls.WriteAsync(Encoding.ASCII.GetBytes("GET /_drava/live HTTP/1.1\r\nHost: " + host + "\r\nConnection: close\r\n\r\n"), cancellationToken).ConfigureAwait(false);
            var response = new byte[256];
            var count = await tls.ReadAsync(response, cancellationToken).ConfigureAwait(false);
            Assert.StartsWith("HTTP/1.1 200", Encoding.ASCII.GetString(response, 0, count), StringComparison.Ordinal);
        }
        Assert.False(string.IsNullOrEmpty(fingerprint));
        if (requireIntermediate) Assert.True(receivedIntermediate, "Client supplied only the root; the Gateway must supply the missing intermediate without a network download.");
        return fingerprint!;
    }
}
