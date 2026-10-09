using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class GatewayInformationalResponseTests
{
    private static readonly int[] ExpectedStatuses = [102, 103, 200];
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpstreamEarlyHeadsCrossBothProcessesBeforeTheFinalResponseAsync(bool http2)
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        using var upstream = new DevelopmentHttp2Peer(certificate);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: http2, upstreamHttp2: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = new TcpClient();
        using var verifier = http2 ? new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test") : null;
        await socket.ConnectAsync(IPAddress.Loopback, http2 ? proxy.TlsPort : proxy.Port, deadline.Token).ConfigureAwait(true);
        var network = socket.GetStream();
        await using var networkLifetime = network.ConfigureAwait(true);
        var tls = new SslStream(network, leaveInnerStreamOpen: true);
        await using var tlsLifetime = tls.ConfigureAwait(true);
        if (http2)
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "svc.site.test", ApplicationProtocols = [SslApplicationProtocol.Http2],
                RemoteCertificateValidationCallback = verifier!.ValidateServer,
            }, deadline.Token).ConfigureAwait(true);
        Stream stream = http2 ? tls : network;
        var peer = upstream.RespondInformationalAsync(deadline.Token);
        try
        {
            if (http2) await VerifyHttp2Async(stream, deadline.Token).ConfigureAwait(true);
            else await VerifyHttp1Async(stream, deadline.Token).ConfigureAwait(true);
            await peer.ConfigureAwait(true);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException) { }
        }
    }

    private static async Task VerifyHttp1Async(Stream stream, CancellationToken token)
    {
        await stream.WriteAsync("GET /early HTTP/1.1\r\nHost: svc.site.test\r\nConnection: close\r\n\r\n"u8.ToArray(), token).ConfigureAwait(false);
        using var output = new MemoryStream();
        await stream.CopyToAsync(output, token).ConfigureAwait(false);
        var text = Encoding.UTF8.GetString(output.ToArray());
        var heads = text.Split("\r\n\r\n", StringSplitOptions.None);
        Assert.Equal(4, heads.Length);
        Assert.StartsWith("HTTP/1.1 102 Informational\r\n", heads[0], StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 103 Informational\r\n", heads[1], StringComparison.Ordinal);
        Assert.Contains("link: </style.css>; rel=preload", heads[0], StringComparison.Ordinal);
        Assert.Contains("link: </style.css>; rel=preload", heads[1], StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 200", heads[2], StringComparison.Ordinal);
        Assert.Equal("final", heads[3]);
    }

    private static async Task VerifyHttp2Async(Stream stream, CancellationToken token)
    {
        await stream.WriteAsync("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray(), token).ConfigureAwait(false);
        await Http2TestFrames.WriteAsync(stream, Http2TestFrameType.Settings, 0, 0, new byte[] { 0, 1, 0, 0, 0, 0 }, token).ConfigureAwait(false);
        var block = HpackCodec.EncodeRequestHeaders([new(":method", "GET"), new(":scheme", "https"), new(":path", "/early"), new(":authority", "svc.site.test")]);
        await Http2TestFrames.WriteAsync(stream, Http2TestFrameType.Headers, 5, 1, block, token).ConfigureAwait(false);
        List<int> statuses = [];
        using var body = new MemoryStream();
        var first = true;
        while (true)
        {
            var frame = await Http2TestFrames.ReadAsync(stream, token).ConfigureAwait(false);
            if (first) Assert.Equal(Http2TestFrameType.Settings, frame.Type);
            first = false;
            if (frame.Type == Http2TestFrameType.Settings && frame.Flags == 0)
                await Http2TestFrames.WriteAsync(stream, Http2TestFrameType.Settings, 1, 0, ReadOnlyMemory<byte>.Empty, token).ConfigureAwait(false);
            if (frame.StreamId != 1) continue;
            Assert.NotEqual(Http2TestFrameType.RstStream, frame.Type);
            if (frame.Type == Http2TestFrameType.Headers)
            {
                Assert.True((frame.Flags & 4) != 0);
                Assert.True(HpackCodec.TryDecodeResponseHeaders(frame.Payload.ToArray(), 32768, 128, out var fields, out var reason), reason);
                statuses.Add(int.Parse(fields.Single(static field => field.Name.Equals(":status", StringComparison.Ordinal)).Value,
                    System.Globalization.CultureInfo.InvariantCulture));
            }
            if (frame.Type == Http2TestFrameType.Data) body.Write(frame.Payload.Span);
            if (frame.Type is Http2TestFrameType.Headers or Http2TestFrameType.Data && (frame.Flags & 1) != 0) break;
        }
        Assert.Equal(ExpectedStatuses, statuses);
        Assert.Equal("final"u8.ToArray(), body.ToArray());
    }
}
