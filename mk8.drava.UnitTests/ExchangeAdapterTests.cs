using System.Text;
using Google.Protobuf;
using Mk8.Drava.Application.INF.Proxy.Exchange;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ExchangeAdapterTests
{
    [Theory]
    [InlineData("HTTP/1.1", "http1")]
    [InlineData("HTTP/2", "http2")]
    [InlineData("HTTP/3", "http3")]
    public void IncomingVersionsPreserveProtocolMetricsAcrossTheVirtualHttp1Adapter(string protocol, string expected)
    {
        var request = new RequestHead { Version = FrameLimits.Version, ExchangeId = Guid.NewGuid().ToString("N"), GatewayId = "gateway", GatewayGeneration = 1,
            ListenerId = "http", Method = "GET", RawTarget = "/", Authority = "svc.site.test", Scheme = "http", ClientProtocol = protocol,
            PeerAddress = "127.0.0.1", PeerPort = 12345 };
        Assert.Equal(expected, ExchangeRequestMapper.ToRequest(request).ClientProtocol);
    }

    [Fact]
    public async Task DecodesFragmentedInformationalChunkedResponseAndTrailersAsync()
    {
        var writer = new ExchangeFrameWriter();
        var stopped = false;
        using var decoder = new ExchangeResponseWriter(writer, "GET", _ => { stopped = true; return ValueTask.CompletedTask; });
        var bytes = "HTTP/1.1 103 Early Hints\r\nLink: </a>; rel=preload\r\n\r\nHTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nTrailer: grpc-status\r\nConnection: keep-alive\r\n\r\n3\r\nabc\r\n2\r\nde\r\n0\r\ngrpc-status: 0\r\n\r\n"u8.ToArray();
        foreach (var value in bytes) await decoder.WriteAsync(new byte[] { value }, CancellationToken.None).ConfigureAwait(true);
        await decoder.CompleteAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.False(stopped);
        var heads = writer.Frames.Where(static frame => frame.Response is not null).Select(static frame => frame.Response).ToArray();
        Assert.Equal([103u, 200u], heads.Select(static head => head.StatusCode));
        Assert.DoesNotContain(heads[1].Headers, static header => string.Equals(header.Name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(heads[1].Headers, static header => string.Equals(header.Name, "Connection", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("abcde", Encoding.UTF8.GetString(writer.Frames.Where(static frame => frame.Data is not null).SelectMany(static frame => frame.Data.Payload.ToByteArray()).ToArray()));
        Assert.Collection(writer.Frames.Where(static frame => frame.Trailers is not null),
            frame => Assert.Collection(frame.Trailers.Headers, header => Assert.Equal("0", header.Value)));
        using var digest = new BodyDigest();
        digest.Append("abcde"u8);
        digest.Verify(writer.Frames[^1].Complete);
    }

    [Theory]
    [InlineData("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nabc")]
    [InlineData("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n4\r\nabc")]
    [InlineData("HTTP/1.1 103 Early Hints\r\n\r\n")]
    public async Task RejectsForwarderCompletionWithTruncatedResponseAsync(string response)
    {
        var writer = new ExchangeFrameWriter();
        using var decoder = new ExchangeResponseWriter(writer, "GET", static _ => ValueTask.CompletedTask);
        await decoder.WriteAsync(Encoding.ASCII.GetBytes(response), CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => decoder.CompleteAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.DoesNotContain(writer.Frames, static frame => frame.Complete is not null);
    }

    [Fact]
    public async Task PreservesHeadRepresentationLengthWithoutBodyAsync()
    {
        var writer = new ExchangeFrameWriter();
        using var decoder = new ExchangeResponseWriter(writer, "HEAD", static _ => ValueTask.CompletedTask);
        await decoder.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 321\r\n\r\n"u8.ToArray(), CancellationToken.None).ConfigureAwait(true);
        await decoder.CompleteAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Collection(writer.Frames[0].Response.Headers, header => Assert.Equal("321", header.Value));
        Assert.Equal(0ul, writer.Frames[^1].Complete.BodyBytes);
    }

    [Fact]
    public async Task NormalizesBodyFramesAndTrailersToBoundedVirtualRequestAsync()
    {
        using var digest = new BodyDigest();
        digest.Append("abc"u8);
        var trailers = new TrailerFrame();
        trailers.Headers.Add(new Header { Name = "x-integrity", Value = "yes" });
        using var reader = new ExchangeFrameReader([
            new ExchangeFrame { Data = new DataFrame { Payload = ByteString.CopyFromUtf8("abc") } },
            new ExchangeFrame { Trailers = trailers }, new ExchangeFrame { Complete = digest.Complete() }]);
        using var body = new ExchangeRequestBody(reader, new RequestHead { HasBody = true, ContentLength = 3 });
        var output = new StringBuilder();
        var buffer = new byte[2];
        int count;
        while ((count = await body.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(true)) > 0) output.Append(Encoding.ASCII.GetString(buffer, 0, count));
        Assert.Equal("3\r\nabc\r\n0\r\nx-integrity: yes\r\n\r\n", output.ToString());
        Assert.True(body.Completed);
    }

    [Fact]
    public async Task RejectsEofAsUploadCompletionAsync()
    {
        using var reader = new ExchangeFrameReader([]);
        using var body = new ExchangeRequestBody(reader, new RequestHead());
        await Assert.ThrowsAsync<InvalidDataException>(() => body.RequireCompletionAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
    }

    [Fact]
    public async Task RejectsUnsolicitedUploadStopAcknowledgmentAsync()
    {
        using var reader = new ExchangeFrameReader([new ExchangeFrame { UploadStopped = new UploadStopped() }]);
        using var body = new ExchangeRequestBody(reader, new RequestHead());
        await Assert.ThrowsAsync<InvalidDataException>(() => body.RequireCompletionAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
    }

    [Fact]
    public async Task RequiresExplicitStopAcknowledgmentAfterEarlyFinalResponseAsync()
    {
        using var reader = new ExchangeFrameReader([new ExchangeFrame { UploadStopped = new UploadStopped() }]);
        var writer = new ExchangeFrameWriter();
        using var stream = new ExchangeClientStream(reader, writer, new RequestHead { Method = "POST", HasBody = true });
        await stream.WriteAsync("HTTP/1.1 413 Payload Too Large\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), CancellationToken.None).ConfigureAwait(true);
        await stream.CompleteAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(writer.Frames[0].StopUpload);
        Assert.Equal(413u, writer.Frames[1].Response.StatusCode);
        Assert.NotNull(writer.Frames[^1].Complete);
    }

    [Fact]
    public async Task RejectsForbiddenResponseTrailersAsync()
    {
        var writer = new ExchangeFrameWriter();
        using var decoder = new ExchangeResponseWriter(writer, "GET", static _ => ValueTask.CompletedTask);
        await decoder.WriteAsync("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n0\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => decoder.CompleteAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
    }
}
