using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
public sealed class UpstreamHttp3EarlyRejectionProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualQuicRejectionReachesTheClientBeforeAnyDeclaredUploadBodyAsync(bool clientHttp2)
    {
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var listener = await DevelopmentPendingQuicPeer.CreateAsync(setup.Token).ConfigureAwait(true);
        await using var listenerLifetime = listener.ConfigureAwait(true);
        listener.ReleaseHandshake();
        var proxy = await TwoProcessProxy.StartAsync(listener.Port, "svc.site.test", enrolledSite: clientHttp2, upstreamHttp3: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var peer = new DevelopmentHttp3DuplexPeer(listener);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = peer.RespondAsync(true, 2 * 1024 * 1024, deadline.Token);
        try
        {
            using var socket = new TcpClient();
            await socket.ConnectAsync(IPAddress.Loopback, clientHttp2 ? proxy.TlsPort : proxy.Port, deadline.Token).ConfigureAwait(true);
            var network = socket.GetStream();
            await using var networkLifetime = network.ConfigureAwait(true);
            if (clientHttp2)
            {
                await ReadHttp2RejectionAsync(network, proxy.RootCertificatePath, proxy.TlsPort, deadline.Token).ConfigureAwait(true);
            }
            else
            {
                await network.WriteAsync("POST /reject HTTP/1.1\r\nHost: svc.site.test\r\nContent-Length: 2097152\r\n\r\n"u8.ToArray(), deadline.Token).ConfigureAwait(true);
                Assert.StartsWith("HTTP/1.1 413 ", await ReadHeadAsync(network, deadline.Token).ConfigureAwait(true), StringComparison.Ordinal);
                var body = new byte[8];
                await network.ReadExactlyAsync(body, deadline.Token).ConfigureAwait(true);
                Assert.Equal("rejected"u8.ToArray(), body);
            }
            peer.FinishResponse();
            await response.ConfigureAwait(true);
        }
        finally
        {
            peer.FinishResponse();
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await response.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or QuicException) { }
        }
    }

    private static async Task ReadHttp2RejectionAsync(Stream network, string rootCertificatePath, int port, CancellationToken cancellationToken)
    {
        using var trust = new DevelopmentSiteClient(rootCertificatePath, port, "svc.site.test");
        var tls = new SslStream(network, leaveInnerStreamOpen: true);
        await using var tlsLifetime = tls.ConfigureAwait(true);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "svc.site.test",
            ApplicationProtocols = [SslApplicationProtocol.Http2], RemoteCertificateValidationCallback = trust.ValidateServer }, cancellationToken).ConfigureAwait(true);
        Assert.Equal(SslApplicationProtocol.Http2, tls.NegotiatedApplicationProtocol);
        var connection = new Http2UpstreamConnection(tls, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        await connection.InitializeAsync(timeouts, cancellationToken).ConfigureAwait(true);
        await connection.SendHeadersAsync([new(":method", "POST"), new(":scheme", "https"), new(":authority", "svc.site.test"),
            new(":path", "/reject"), new("content-length", "2097152")], false, timeouts, cancellationToken).ConfigureAwait(true);
        Assert.Equal(413, (await connection.ReadResponseHeadAsync(16384, timeouts, cancellationToken).ConfigureAwait(true)).StatusCode);
        using var body = new MemoryStream();
        Http2UpstreamDataChunk data;
        do { data = await connection.ReadDataAsync(timeouts, cancellationToken).ConfigureAwait(true); body.Write(data.Data); } while (!data.EndStream);
        Assert.Equal("rejected"u8.ToArray(), body.ToArray());
    }

    private static async Task<string> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new byte[32768];
        var length = 0;
        while (length < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(length, 1), cancellationToken).ConfigureAwait(true);
            if (count == 0) throw new EndOfStreamException("Expected a complete HTTP head.");
            length += count;
            if (length >= 4 && bytes.AsSpan(length - 4, 4).SequenceEqual("\r\n\r\n"u8)) return Encoding.ASCII.GetString(bytes, 0, length);
        }
        throw new InvalidDataException("HTTP head exceeded its bound.");
    }
}
