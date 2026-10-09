using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Mk8.Drava.Presentation.Proxy;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class GatewayInformationalWireTests
{
    private static readonly int[] ExpectedStatuses = [103, 200];
    private static readonly int[] Streams = [1, 3];
    private static readonly string LargeLink = new('x', 30000);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualPublicTlsAdapterPreservesPipelineBodiesAndConcurrentHeaderBlocksAsync(bool http2)
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        var server = await DevelopmentHttpUpstream.StartAsync(RespondAsync, certificate,
            configureListener: GatewayInformationalResponses.Configure).ConfigureAwait(true);
        await using var serverLifetime = server.ConfigureAwait(true);
        var rootPath = Path.Combine(Path.GetTempPath(), "drava-early-" + Guid.NewGuid().ToString("N") + ".der");
        await File.WriteAllBytesAsync(rootPath, certificate.RawData).ConfigureAwait(true);
        try { await VerifyTlsAsync(server.Port, rootPath, http2).ConfigureAwait(true); }
        finally { File.Delete(rootPath); }
    }

    private static async Task RespondAsync(HttpContext context)
    {
        if (context.Request.Path == "/first")
        {
            context.Response.ContentLength = 32000;
            await context.Response.WriteAsync(new string('p', 32000), context.RequestAborted).ConfigureAwait(false);
            return;
        }
        var feature = context.Features.Get<IGatewayInformationalResponseFeature>() ?? throw new InvalidOperationException("Missing production feature.");
        var head = new ResponseHead { StatusCode = 103, Informational = true };
        head.Headers.Add(new Header { Name = "Link", Value = LargeLink });
        await feature.WriteAsync(head, context.Features.Get<IHttp2StreamIdFeature>()?.StreamId ?? 0, context.RequestAborted).ConfigureAwait(false);
        context.Response.ContentLength = 5;
        await context.Response.WriteAsync("final", context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task VerifyTlsAsync(int port, string rootPath, bool http2)
    {
        using var verifier = new DevelopmentSiteClient(rootPath, port, "upstream.test");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, port, deadline.Token).ConfigureAwait(false);
        var network = socket.GetStream();
        await using var networkLifetime = network.ConfigureAwait(false);
        var tls = new SslStream(network, leaveInnerStreamOpen: true);
        await using var tlsLifetime = tls.ConfigureAwait(false);
        var protocol = http2 ? SslApplicationProtocol.Http2 : SslApplicationProtocol.Http11;
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "upstream.test", ApplicationProtocols = [protocol], RemoteCertificateValidationCallback = verifier.ValidateServer,
        }, deadline.Token).ConfigureAwait(false);
        Assert.Equal(protocol, tls.NegotiatedApplicationProtocol);
        if (http2) await VerifyHttp2Async(tls, deadline.Token).ConfigureAwait(false);
        else await VerifyPipelineAsync(tls, deadline.Token).ConfigureAwait(false);
    }

    private static async Task VerifyPipelineAsync(Stream stream, CancellationToken token)
    {
        await stream.WriteAsync("GET /first HTTP/1.1\r\nHost: upstream.test\r\n\r\nGET /early HTTP/1.1\r\nHost: upstream.test\r\nConnection: close\r\n\r\n"u8.ToArray(), token).ConfigureAwait(false);
        using var output = new MemoryStream();
        await stream.CopyToAsync(output, token).ConfigureAwait(false);
        var text = Encoding.ASCII.GetString(output.ToArray());
        Assert.StartsWith("HTTP/1.1 200", text, StringComparison.Ordinal);
        var body = text.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        Assert.True(body >= 4);
        Assert.Equal(new string('p', 32000), text.Substring(body, 32000));
        var early = body + 32000;
        var head = "HTTP/1.1 103 Informational\r\nLink: " + LargeLink + "\r\n\r\n";
        Assert.Equal(head, text.Substring(early, head.Length));
        Assert.StartsWith("HTTP/1.1 200", text[(early + head.Length)..], StringComparison.Ordinal);
        Assert.EndsWith("\r\n\r\nfinal", text, StringComparison.Ordinal);
    }

    private static async Task VerifyHttp2Async(Stream stream, CancellationToken token)
    {
        await stream.WriteAsync("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray(), token).ConfigureAwait(false);
        await Http2TestFrames.WriteAsync(stream, Http2TestFrameType.Settings, 0, 0, new byte[] { 0, 1, 0, 0, 0, 0 }, token).ConfigureAwait(false);
        var request = HpackCodec.EncodeRequestHeaders([new(":method", "GET"), new(":scheme", "https"), new(":path", "/early"), new(":authority", "upstream.test")]);
        foreach (var id in Streams) await Http2TestFrames.WriteAsync(stream, Http2TestFrameType.Headers, 5, id, request, token).ConfigureAwait(false);
        var statuses = new Dictionary<int, List<int>> { [1] = [], [3] = [] };
        var bodies = new Dictionary<int, List<byte>> { [1] = [], [3] = [] };
        using var headers = new MemoryStream();
        var pendingStream = 0;
        var continuations = 0;
        HashSet<int> completed = [];
        while (completed.Count != Streams.Length)
        {
            var frame = await Http2TestFrames.ReadAsync(stream, token).ConfigureAwait(false);
            if (pendingStream != 0)
            {
                Assert.Equal(Http2TestFrameType.Continuation, frame.Type);
                Assert.Equal(pendingStream, frame.StreamId);
            }
            if (frame.Type == Http2TestFrameType.Settings && frame.Flags == 0)
                await Http2TestFrames.WriteAsync(stream, Http2TestFrameType.Settings, 1, 0, ReadOnlyMemory<byte>.Empty, token).ConfigureAwait(false);
            if (frame.StreamId == 0) continue;
            Assert.Contains(frame.StreamId, Streams);
            Assert.NotEqual(Http2TestFrameType.RstStream, frame.Type);
            if (frame.Type is Http2TestFrameType.Headers or Http2TestFrameType.Continuation)
            {
                headers.Write(frame.Payload.Span);
                if (frame.Type == Http2TestFrameType.Continuation) continuations++;
                pendingStream = (frame.Flags & 4) == 0 ? frame.StreamId : 0;
                if (pendingStream == 0)
                {
                    Assert.True(HpackCodec.TryDecodeResponseHeaders(headers.ToArray(), 32768, 128, out var fields, out var reason), reason);
                    var status = int.Parse(fields.Single(static field => field.Name.Equals(":status", StringComparison.Ordinal)).Value, System.Globalization.CultureInfo.InvariantCulture);
                    statuses[frame.StreamId].Add(status);
                    if (status == 103) Assert.Equal(LargeLink, fields.Single(static field => field.Name.Equals("link", StringComparison.Ordinal)).Value);
                    headers.SetLength(0);
                }
            }
            if (frame.Type == Http2TestFrameType.Data) bodies[frame.StreamId].AddRange(frame.Payload.ToArray());
            if (frame.Type is Http2TestFrameType.Data or Http2TestFrameType.Headers && (frame.Flags & 1) != 0) completed.Add(frame.StreamId);
        }
        Assert.Equal(2, continuations);
        AssertCompletedStreams(statuses, bodies);
    }

    private static void AssertCompletedStreams(Dictionary<int, List<int>> statuses, Dictionary<int, List<byte>> bodies)
    {
        foreach (var id in Streams)
        {
            Assert.Equal(ExpectedStatuses, statuses[id]);
            Assert.Equal("final"u8.ToArray(), bodies[id]);
        }
    }
}
