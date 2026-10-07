using System.Buffers.Binary;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class Http2UpstreamTransportTests
{
    [Fact]
    public async Task ReceivedCreditIncludesPaddingAndIsReturnedAfterConsumptionAsync()
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
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Data, 8, 1,
            new byte[] { 3, 11, 12, 13, 0, 0, 0 }, pair.Deadline.Token).ConfigureAwait(true);
        var data = await connection.ReadDataAsync(timeouts, pair.Deadline.Token).ConfigureAwait(true);
        Assert.Equal(new byte[] { 11, 12, 13 }, data.Data);
        Assert.Equal(0, pair.Server.Socket.Available);
        var next = connection.ReadDataAsync(timeouts, pair.Deadline.Token).AsTask();
        try
        {
            for (var index = 0; index < 2; index++)
            {
                var credit = await Http2TestFrames.ReadAsync(pair.Server, pair.Deadline.Token).ConfigureAwait(true);
                Assert.Equal(Http2TestFrameType.WindowUpdate, credit.Type);
                Assert.Equal(index, credit.StreamId);
                Assert.Equal(7u, BinaryPrimitives.ReadUInt32BigEndian(credit.Payload.Span));
            }
            await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Data, Http2TestFlags.EndStream, 1,
                ReadOnlyMemory<byte>.Empty, pair.Deadline.Token).ConfigureAwait(true);
            Assert.True((await next.ConfigureAwait(true)).EndStream);
        }
        finally
        {
            await pair.Deadline.CancelAsync().ConfigureAwait(true);
            try { await next.ConfigureAwait(true); }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task APeerCannotOverrunTheAdvertisedReceiveWindowAsync()
    {
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        await pair.InitializeAsync(connection, initialWindow: 0).ConfigureAwait(true);
        await pair.RequestAsync(connection, hasBody: true).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, Http2TestFlags.EndHeaders, 1,
            HpackCodec.EncodeResponseHeaders(200, []), pair.Deadline.Token).ConfigureAwait(true);
        await connection.ReadResponseHeadAsync(4096, RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5)), pair.Deadline.Token).ConfigureAwait(true);
        // Keep consumption stopped. The fourth frame exceeds 65535 bytes and
        // releases an independently blocked sender with the receiver's failure.
        await Assert.ThrowsAsync<Http2UpstreamProtocolException>(async () =>
        {
            var send = connection.SendDataAsync(new byte[1], false,
                RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5)), pair.Deadline.Token).AsTask();
            for (var index = 0; index < 4; index++)
                await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Data, 0, 1, new byte[16384], pair.Deadline.Token).ConfigureAwait(true);
            await send.ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task SendingBeyondTheInitialWindowsWaitsForBothPeerCreditsAsync()
    {
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        await pair.InitializeAsync(connection).ConfigureAwait(true);
        await pair.RequestAsync(connection, hasBody: true).ConfigureAwait(true);
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        var send = connection.SendDataAsync(new byte[65536], true, timeouts, pair.Deadline.Token).AsTask();
        try
        {
            var received = 0;
            while (received < 65535)
            {
                var frame = await Http2TestFrames.ReadAsync(pair.Server, pair.Deadline.Token).ConfigureAwait(true);
                Assert.Equal(Http2TestFrameType.Data, frame.Type);
                received += frame.Payload.Length;
                Assert.InRange(frame.Payload.Length, 1, 16384);
            }
            Assert.Equal(65535, received);
            Assert.False(send.IsCompleted);
            await pair.WindowAsync(1, 1).ConfigureAwait(true);
            Assert.False(send.IsCompleted);
            await pair.WindowAsync(0, 1).ConfigureAwait(true);
            var final = await Http2TestFrames.ReadAsync(pair.Server, pair.Deadline.Token).ConfigureAwait(true);
            Assert.Equal(1, final.Payload.Length);
            Assert.Equal(Http2TestFlags.EndStream, final.Flags);
            await send.ConfigureAwait(true);
        }
        finally
        {
            await pair.Deadline.CancelAsync().ConfigureAwait(true);
            try { await send.ConfigureAwait(true); }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task MalformedWindowUpdateReleasesABlockedSenderWithTheProtocolFailureAsync()
    {
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        await pair.InitializeAsync(connection, initialWindow: 0).ConfigureAwait(true);
        await pair.RequestAsync(connection, hasBody: true).ConfigureAwait(true);
        await Assert.ThrowsAsync<Http2UpstreamProtocolException>(async () =>
        {
            var send = connection.SendDataAsync(new byte[1], true, RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5)), pair.Deadline.Token).AsTask();
            await pair.WindowAsync(1, 0).ConfigureAwait(true);
            await send.ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteFieldBlocksRejectDuplicateOrLateStatusPseudoHeadersAsync(bool late)
    {
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        await pair.InitializeAsync(connection).ConfigureAwait(true);
        await pair.RequestAsync(connection).ConfigureAwait(true);
        ProxyHeaderField[] headers = late ? [new("content-type", "text/plain"), new(":status", "200")]
            : [new(":status", "200"), new(":status", "201")];
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, 5, 1, HpackCodec.EncodeRequestHeaders(headers), pair.Deadline.Token).ConfigureAwait(true);
        await Assert.ThrowsAsync<Http2UpstreamProtocolException>(async () =>
            await connection.ReadResponseHeadAsync(4096, RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5)), pair.Deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task AControlFrameCannotInterleaveAContinuationSequenceAsync()
    {
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        await pair.InitializeAsync(connection).ConfigureAwait(true);
        await pair.RequestAsync(connection).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, 0, 1, new byte[1], pair.Deadline.Token).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Ping, 0, 0, new byte[8], pair.Deadline.Token).ConfigureAwait(true);
        await Assert.ThrowsAsync<Http2UpstreamProtocolException>(async () =>
            await connection.ReadResponseHeadAsync(4096, RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5)), pair.Deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task EndStreamOnTheFirstFragmentSurvivesReassemblyAfterAnInterimResponseAsync()
    {
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        await pair.InitializeAsync(connection).ConfigureAwait(true);
        await pair.RequestAsync(connection).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, Http2TestFlags.EndHeaders, 1,
            HpackCodec.EncodeResponseHeaders(100, []), pair.Deadline.Token).ConfigureAwait(true);
        var final = HpackCodec.EncodeResponseHeaders(200, [new ProxyHeaderField("content-type", "text/plain")]);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, Http2TestFlags.EndStream, 1, final.AsMemory(0, 1), pair.Deadline.Token).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Continuation, Http2TestFlags.EndHeaders, 1, final.AsMemory(1), pair.Deadline.Token).ConfigureAwait(true);
        var response = await connection.ReadResponseHeadAsync(4096, RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5)), pair.Deadline.Token).ConfigureAwait(true);
        Assert.Equal(200, response.StatusCode);
        Assert.True(response.EndStream);
        Assert.Collection(response.Headers, static header => Assert.Equal("text/plain", header.Value));
    }
}
