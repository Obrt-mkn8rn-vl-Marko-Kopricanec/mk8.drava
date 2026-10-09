using System.Net.Quic;
using System.Runtime.Versioning;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
public sealed class Http3FrameBoundaryTests
{
    [Theory]
    [InlineData(2, "head")]
    [InlineData(2, "body")]
    [InlineData(2, "trailers")]
    [InlineData(3, "head")]
    [InlineData(3, "body")]
    [InlineData(3, "trailers")]
    [InlineData(4, "head")]
    [InlineData(4, "body")]
    [InlineData(4, "trailers")]
    [InlineData(5, "head")]
    [InlineData(5, "body")]
    [InlineData(5, "trailers")]
    [InlineData(6, "head")]
    [InlineData(6, "body")]
    [InlineData(6, "trailers")]
    [InlineData(7, "head")]
    [InlineData(7, "body")]
    [InlineData(7, "trailers")]
    [InlineData(8, "head")]
    [InlineData(8, "body")]
    [InlineData(8, "trailers")]
    [InlineData(9, "head")]
    [InlineData(9, "body")]
    [InlineData(9, "trailers")]
    [InlineData(13, "head")]
    [InlineData(13, "body")]
    [InlineData(13, "trailers")]
    public async Task ActualQuicRejectsReservedAndUnnegotiatedKnownFramesInResponseStreamsAsync(long type, string phase)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var fixture = await DevelopmentOwnedQuicPeer.CreateAsync(processor: null, deadline.Token).ConfigureAwait(true);
        await using var fixtureLifetime = fixture.ConfigureAwait(true);
        Assert.True(fixture.Connection.TryReserveStream(TimeSpan.FromMinutes(1)));
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        var client = await Http3UpstreamConnection.OpenStreamAsync(fixture.Connection, timeouts, fixture.Metrics, 65536, deadline.Token).ConfigureAwait(true);
        await using var clientLifetime = client.ConfigureAwait(true);
        var peer = RespondAsync(fixture, phase, type, 1, 0, false, deadline.Token);
        try
        {
            await client.SendHeadersAsync([new(":method", "GET"), new(":scheme", "https"), new(":authority", "upstream.test"), new(":path", "/known-frame")], true, timeouts, deadline.Token).ConfigureAwait(true);
            if (string.Equals(phase, "head", StringComparison.Ordinal))
                await Assert.ThrowsAsync<Http3UpstreamProtocolException>(async () =>
                    await client.ReadResponseHeadAsync(1024, timeouts, deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
            else
            {
                Assert.Equal(200, (await client.ReadResponseHeadAsync(1024, timeouts, deadline.Token).ConfigureAwait(true)).StatusCode);
                Assert.Equal("abc"u8.ToArray(), (await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true)).Data);
                await Assert.ThrowsAsync<Http3UpstreamProtocolException>(async () =>
                    await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
            }
            Assert.Equal(Http3UpstreamPooledConnectionState.Failed, fixture.Connection.State);
            await peer.ConfigureAwait(true);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or QuicException) { }
        }
    }

    [Theory]
    [InlineData("head", 129, 0, false, true)]
    [InlineData("interim", 129, 0, false, true)]
    [InlineData("body", 129, 0, false, true)]
    [InlineData("trailers", 129, 0, false, true)]
    [InlineData("head", 17, 65536, false, true)]
    [InlineData("interim", 17, 65536, false, true)]
    [InlineData("body", 17, 65536, false, true)]
    [InlineData("trailers", 17, 65536, false, true)]
    [InlineData("head", 128, 0, false, false)]
    [InlineData("head", 16, 65536, false, false)]
    [InlineData("head", 129, 0, true, true)]
    [InlineData("head", 17, 65536, true, true)]
    public async Task ActualQuicBoundsIgnoredWorkAcrossTheCompleteResponseAsync(string phase, int count, int payloadBytes, bool spread, bool rejected)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var fixture = await DevelopmentOwnedQuicPeer.CreateAsync(processor: null, deadline.Token).ConfigureAwait(true);
        await using var fixtureLifetime = fixture.ConfigureAwait(true);
        Assert.True(fixture.Connection.TryReserveStream(TimeSpan.FromMinutes(1)));
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        var client = await Http3UpstreamConnection.OpenStreamAsync(fixture.Connection, timeouts, fixture.Metrics, 65536, deadline.Token).ConfigureAwait(true);
        await using var clientLifetime = client.ConfigureAwait(true);
        var peer = RespondAsync(fixture, phase, 0x21, count, payloadBytes, spread, deadline.Token);
        try
        {
            await client.SendHeadersAsync([new(":method", "GET"), new(":scheme", "https"), new(":authority", "upstream.test"), new(":path", "/bounded-extension")], true, timeouts, deadline.Token).ConfigureAwait(true);
            if (rejected)
            {
                var exception = await Assert.ThrowsAsync<Http3UpstreamProtocolException>(async () =>
                    await ReadCompleteResponseAsync(client, timeouts, deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
                Assert.Contains("resource bound", exception.Message, StringComparison.Ordinal);
                Assert.Equal(Http3UpstreamPooledConnectionState.Failed, fixture.Connection.State);
            }
            else await ReadCompleteResponseAsync(client, timeouts, deadline.Token).ConfigureAwait(true);
            await peer.ConfigureAwait(true);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or QuicException) { }
        }
    }

    private static async Task ReadCompleteResponseAsync(Http3UpstreamConnection client, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        Assert.Equal(200, (await client.ReadResponseHeadAsync(1024, timeouts, cancellationToken).ConfigureAwait(false)).StatusCode);
        Assert.Equal("abc"u8.ToArray(), (await client.ReadDataAsync(timeouts, cancellationToken).ConfigureAwait(false)).Data);
        var end = await client.ReadDataAsync(timeouts, cancellationToken).ConfigureAwait(false);
        Assert.True(end.EndStream);
        Assert.Collection(Assert.IsAssignableFrom<IReadOnlyList<ProxyHeaderField>>(end.Trailers),
            static field => Assert.Equal(new ProxyHeaderField("x-end", "done"), field));
    }

    private static async Task RespondAsync(DevelopmentOwnedQuicPeer fixture, string phase, long type, int count, int payloadBytes, bool spread, CancellationToken cancellationToken)
    {
        var stream = await fixture.AcceptRequestAsync(cancellationToken).ConfigureAwait(false);
        await using var streamLifetime = stream.ConfigureAwait(false);
        using var request = new MemoryStream();
        await stream.CopyToAsync(request, cancellationToken).ConfigureAwait(false);
        using var response = new MemoryStream();
        var payload = new byte[payloadBytes];
        var first = spread ? count / 2 : count;
        AddFrames(response, phase, "head", type, first, payload);
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([new(":status", "103")]));
        AddFrames(response, phase, "interim", type, first, payload);
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([new(":status", "200"), new("content-length", "3")]));
        Http3Codec.WriteFrame(response, Http3Codec.DataFrame, "abc"u8);
        AddFrames(response, phase, "body", type, first, payload);
        if (spread) AddFrames(response, "body", "body", type, count - first - 1, payload);
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([new("x-end", "done")]));
        AddFrames(response, phase, "trailers", type, first, payload);
        if (spread) AddFrames(response, "trailers", "trailers", type, 1, payload);
        await stream.WriteAsync(response.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
    }

    private static void AddFrames(Stream stream, string selected, string phase, long type, int count, ReadOnlySpan<byte> payload)
    {
        if (!string.Equals(selected, phase, StringComparison.Ordinal)) return;
        for (var index = 0; index < count; index++) Http3Codec.WriteFrame(stream, type, payload);
    }
}
