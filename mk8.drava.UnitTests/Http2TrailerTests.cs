using System.Text;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.INF.Proxy.Exchange;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class Http2TrailerTests
{
    [Theory]
    [InlineData("trailers", true)]
    [InlineData("Trailers", true)]
    [InlineData("gzip", false)]
    public void Http2PreservesOnlyTheLegalTeValue(string value, bool allowed)
    {
        var policy = new HopByHopHeaderPolicy();
        var fields = policy.FilterForForwarding([new("te", value)], false, false, preserveTeTrailers: true);
        Assert.Equal(allowed ? 1 : 0, fields.Count);
        var block = HpackCodec.EncodeRequestHeaders([new("te", value)]);
        Assert.True(HpackCodec.TryDecodeRequestHeaders(block, out var decoded, out _));
        Assert.Equal(allowed ? 1 : 0, decoded.Count);
        Assert.Empty(policy.FilterForForwarding([new("Connection", "te"), new("te", value)], false, false, preserveTeTrailers: true));
        Assert.False(Http2HeaderPolicy.IsForbiddenResponseHeader("trailer"));
        Assert.True(Http2HeaderPolicy.IsForbiddenResponseHeader("te"));
        Assert.True(Http2HeaderPolicy.IsForbiddenResponseHeader("connection"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndingFieldsSurviveFragmentationAndFixedLengthBodiesAsync(bool fragmented)
    {
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        await pair.InitializeAsync(connection).ConfigureAwait(true);
        await pair.RequestAsync(connection).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, Http2TestFlags.EndHeaders, 1,
            HpackCodec.EncodeResponseHeaders(200, [new("content-length", "3")]), pair.Deadline.Token).ConfigureAwait(true);
        await connection.ReadResponseHeadAsync(4096, timeouts, pair.Deadline.Token).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Data, 0, 1, "abc"u8.ToArray(), pair.Deadline.Token).ConfigureAwait(true);
        var fields = HpackCodec.EncodeRequestHeaders([new("grpc-status", "7"), new("grpc-message", "denied%20here")]);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers,
            fragmented ? Http2TestFlags.EndStream : (byte)(Http2TestFlags.EndStream | Http2TestFlags.EndHeaders), 1,
            fragmented ? fields.AsMemory(0, 1) : fields, pair.Deadline.Token).ConfigureAwait(true);
        if (fragmented) await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Continuation, Http2TestFlags.EndHeaders, 1,
            fields.AsMemory(1), pair.Deadline.Token).ConfigureAwait(true);
        Assert.Equal("abc"u8.ToArray(), (await connection.ReadDataAsync(timeouts, pair.Deadline.Token).ConfigureAwait(true)).Data);
        var end = await connection.ReadDataAsync(timeouts, pair.Deadline.Token).ConfigureAwait(true);
        Assert.True(end.EndStream);
        Assert.Empty(end.Data);
        Assert.Collection(Assert.IsAssignableFrom<IReadOnlyList<ProxyHeaderField>>(end.Trailers),
            static field => Assert.Equal(new ProxyHeaderField("grpc-status", "7"), field),
            static field => Assert.Equal(new ProxyHeaderField("grpc-message", "denied%20here"), field));
    }

    [Theory]
    [InlineData(":status", "200")]
    [InlineData("content-length", "3")]
    [InlineData("authorization", "private")]
    [InlineData("Connection", "close")]
    public async Task APeerCannotInjectFramingIdentityOrPseudoHeadersInTrailersAsync(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        await pair.InitializeAsync(connection).ConfigureAwait(true);
        await pair.RequestAsync(connection).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, Http2TestFlags.EndHeaders, 1,
            HpackCodec.EncodeResponseHeaders(200, []), pair.Deadline.Token).ConfigureAwait(true);
        await connection.ReadResponseHeadAsync(4096, timeouts, pair.Deadline.Token).ConfigureAwait(true);
        // Literal wire encoding retains the forbidden uppercase/hop-by-hop name;
        // the normal encoder deliberately filters those outgoing names.
        using var block = new MemoryStream();
        block.WriteByte(0); block.WriteByte((byte)name.Length); block.Write(Encoding.ASCII.GetBytes(name));
        block.WriteByte((byte)value.Length); block.Write(Encoding.ASCII.GetBytes(value));
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, 5, 1, block.ToArray(), pair.Deadline.Token).ConfigureAwait(true);
        await Assert.ThrowsAsync<Http2UpstreamProtocolException>(async () =>
            await connection.ReadDataAsync(timeouts, pair.Deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task TrailingHeadersMustEndTheStreamAsync()
    {
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        await pair.InitializeAsync(connection).ConfigureAwait(true);
        await pair.RequestAsync(connection).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, Http2TestFlags.EndHeaders, 1,
            HpackCodec.EncodeResponseHeaders(200, []), pair.Deadline.Token).ConfigureAwait(true);
        await connection.ReadResponseHeadAsync(4096, timeouts, pair.Deadline.Token).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, Http2TestFlags.EndHeaders, 1,
            HpackCodec.EncodeRequestHeaders([new("grpc-status", "0")]), pair.Deadline.Token).ConfigureAwait(true);
        await Assert.ThrowsAsync<Http2UpstreamProtocolException>(async () =>
            await connection.ReadDataAsync(timeouts, pair.Deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task TrailerCountIsBoundedBeforeItReachesTheExchangeAsync()
    {
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        await pair.InitializeAsync(connection).ConfigureAwait(true);
        await pair.RequestAsync(connection).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, Http2TestFlags.EndHeaders, 1,
            HpackCodec.EncodeResponseHeaders(200, []), pair.Deadline.Token).ConfigureAwait(true);
        await connection.ReadResponseHeadAsync(4096, timeouts, pair.Deadline.Token).ConfigureAwait(true);
        var fields = Enumerable.Repeat(new ProxyHeaderField("x-trailer", "v"), 129).ToArray();
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, 5, 1,
            HpackCodec.EncodeRequestHeaders(fields), pair.Deadline.Token).ConfigureAwait(true);
        await Assert.ThrowsAsync<Http2UpstreamProtocolException>(async () =>
            await connection.ReadDataAsync(timeouts, pair.Deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedTrailersFollowTheCompleteBodyWithoutLosingUnicodeAsync(bool chunked)
    {
        var writer = new ExchangeFrameWriter();
        using var decoder = new ExchangeResponseWriter(writer, "GET", static _ => ValueTask.CompletedTask);
        var response = chunked ? "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n3\r\nabc\r\n0\r\n\r\n"
            : "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\nabc";
        await decoder.WriteAsync(Encoding.ASCII.GetBytes(response), CancellationToken.None).ConfigureAwait(true);
        decoder.SetTrailers([new("grpc-status", "0"), new("grpc-message", "ok Ω")]);
        await decoder.CompleteAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("ok Ω", Assert.IsType<TrailerFrame>(writer.Frames[^2].Trailers).Headers[1].Value);
        Assert.Equal(3ul, Assert.IsType<Completion>(writer.Frames[^1].Complete).BodyBytes);
        Assert.Throws<InvalidDataException>(() => decoder.SetTrailers([new("x-late", "no")]));
    }

    [Fact]
    public async Task TypedTrailersCannotFinishATruncatedFixedLengthBodyAsync()
    {
        var writer = new ExchangeFrameWriter();
        using var decoder = new ExchangeResponseWriter(writer, "GET", static _ => ValueTask.CompletedTask);
        await decoder.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nabc"u8.ToArray(), CancellationToken.None).ConfigureAwait(true);
        Assert.Throws<InvalidDataException>(() => decoder.SetTrailers([new("grpc-status", "0")]));
        Assert.DoesNotContain(writer.Frames, static frame => frame.Complete is not null || frame.Trailers is not null);
    }
}
