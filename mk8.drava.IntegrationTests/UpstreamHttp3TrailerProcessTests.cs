using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
public sealed class UpstreamHttp3TrailerProcessTests
{
    [Fact]
    public async Task RequestAndResponseTrailersCrossBothProcessesAndTheActualQuicUpstreamAsync() =>
        await VerifyAsync(VerifyEchoTrailersAsync, requests: 1, requestTrailers: true, cache: false).ConfigureAwait(true);

    [Fact]
    public async Task TrailerBearingFixedLengthQuicResponsesAreExcludedFromTheBodyOnlyCacheAsync() =>
        await VerifyAsync(VerifyCacheTrailersAsync, requests: 2, requestTrailers: false, cache: true).ConfigureAwait(true);

    private static async Task VerifyAsync(Func<TwoProcessProxy, CancellationToken, Task> verify,
        int requests, bool requestTrailers, bool cache)
    {
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var listener = await DevelopmentPendingQuicPeer.CreateAsync(setup.Token).ConfigureAwait(false);
        await using var listenerLifetime = listener.ConfigureAwait(false);
        listener.ReleaseHandshake();
        var peer = new DevelopmentHttp3TrailerPeer(listener);
        var proxy = await TwoProcessProxy.StartAsync(listener.Port, "svc.site.test", enrolledSite: true,
            manualCache: cache, upstreamHttp3: true).ConfigureAwait(false);
        await using var proxyLifetime = proxy.ConfigureAwait(false);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = peer.RespondAsync(requests, requestTrailers, deadline.Token);
        try
        {
            await verify(proxy, deadline.Token).ConfigureAwait(false);
            peer.FinishResponse();
            await response.ConfigureAwait(false);
        }
        finally
        {
            peer.FinishResponse();
            await deadline.CancelAsync().ConfigureAwait(false);
            try { await response.ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or QuicException) { }
        }
    }

    private static async Task VerifyEchoTrailersAsync(TwoProcessProxy proxy, CancellationToken cancellationToken)
    {
        using var trust = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, proxy.TlsPort, cancellationToken).ConfigureAwait(false);
        var network = socket.GetStream();
        await using var networkLifetime = network.ConfigureAwait(false);
        var tls = new SslStream(network, leaveInnerStreamOpen: true);
        await using var tlsLifetime = tls.ConfigureAwait(false);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "svc.site.test",
            ApplicationProtocols = [SslApplicationProtocol.Http2], RemoteCertificateValidationCallback = trust.ValidateServer }, cancellationToken).ConfigureAwait(false);
        Assert.Equal(SslApplicationProtocol.Http2, tls.NegotiatedApplicationProtocol);
        var connection = new Http2UpstreamConnection(tls, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(false);
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        await connection.InitializeAsync(timeouts, cancellationToken).ConfigureAwait(false);
        await connection.SendHeadersAsync([new(":method", "POST"), new(":scheme", "https"), new(":authority", "svc.site.test"),
            new(":path", "/echo"), new("content-type", "application/grpc"), new("te", "trailers")], false, timeouts, cancellationToken).ConfigureAwait(false);
        await connection.SendDataAsync("abc"u8.ToArray(), false, timeouts, cancellationToken).ConfigureAwait(false);
        await connection.SendHeadersAsync([new("x-client-end", "client-complete")], true, timeouts, cancellationToken).ConfigureAwait(false);
        Assert.Equal(200, (await connection.ReadResponseHeadAsync(16384, timeouts, cancellationToken).ConfigureAwait(false)).StatusCode);
        using var body = new MemoryStream();
        Http2UpstreamDataChunk end;
        do
        {
            end = await connection.ReadDataAsync(timeouts, cancellationToken).ConfigureAwait(false);
            body.Write(end.Data);
        } while (!end.EndStream);
        Assert.Equal("abc"u8.ToArray(), body.ToArray());
        Assert.Contains(Assert.IsAssignableFrom<IReadOnlyList<ProxyHeaderField>>(end.Trailers),
            static field => field.Name.Equals("grpc-status", StringComparison.Ordinal) && field.Value.Equals("0", StringComparison.Ordinal));
        Assert.Contains(end.Trailers!, static field => field.Name.Equals("grpc-message", StringComparison.Ordinal)
            && field.Value.Equals("complete%20here", StringComparison.Ordinal));
    }

    private static async Task VerifyCacheTrailersAsync(TwoProcessProxy proxy, CancellationToken cancellationToken)
    {
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        for (var index = 1; index <= 2; index++)
        {
            using var response = await client.Client.GetAsync(new Uri("/cached", UriKind.Relative), cancellationToken).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("abc", await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var expected = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Collection(response.TrailingHeaders.GetValues("x-final-status"), value => Assert.Equal(expected, value));
        }
    }
}
