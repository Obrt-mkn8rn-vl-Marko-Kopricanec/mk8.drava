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
public sealed class Http3InformationalResponseTests
{
    private static readonly int[] EarlyStatuses = [102, 103];
    [Theory]
    [InlineData("final")]
    [InlineData("upgrade")]
    [InlineData("length")]
    [InlineData("limit")]
    [InlineData("order")]
    [InlineData("uppercase")]
    [InlineData("control")]
    [InlineData("fields")]
    public async Task ActualQuicResponseRequiresAFinalHeadAfterBoundedValidEarlyHeadsAsync(string kind)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var fixture = await DevelopmentOwnedQuicPeer.CreateAsync(processor: null, deadline.Token).ConfigureAwait(true);
        await using var fixtureLifetime = fixture.ConfigureAwait(true);
        Assert.True(fixture.Connection.TryReserveStream(TimeSpan.FromMinutes(1)));
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        var client = await Http3UpstreamConnection.OpenStreamAsync(fixture.Connection, timeouts, fixture.Metrics, 16384, deadline.Token).ConfigureAwait(true);
        await using var clientLifetime = client.ConfigureAwait(true);
        var peer = RespondAsync(fixture, kind, deadline.Token);
        try
        {
            await client.SendHeadersAsync([new(":method", "GET"), new(":scheme", "https"), new(":authority", "upstream.test"), new(":path", "/early")], true, timeouts, deadline.Token).ConfigureAwait(true);
            if (string.Equals(kind, "final", StringComparison.Ordinal))
            {
                var head = await client.ReadResponseHeadAsync(4096, timeouts, deadline.Token).ConfigureAwait(true);
                Assert.Equal(200, head.StatusCode);
                var data = await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true);
                Assert.Equal("final"u8.ToArray(), data.Data);
                Assert.True((await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true)).EndStream);
            }
            else
                await Assert.ThrowsAsync<Http3UpstreamProtocolException>(async () =>
                    await client.ReadResponseHeadAsync(4096, timeouts, deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualQuicEarlyHeadCallbacksRemainOrderedAndFailureRetiresTheLeaseAsync(bool callbackFailure)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var fixture = await DevelopmentOwnedQuicPeer.CreateAsync(processor: null, deadline.Token).ConfigureAwait(true);
        await using var fixtureLifetime = fixture.ConfigureAwait(true);
        Assert.True(fixture.Connection.TryReserveStream(TimeSpan.FromMinutes(1)));
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        var client = await Http3UpstreamConnection.OpenStreamAsync(fixture.Connection, timeouts, fixture.Metrics, 16384, deadline.Token).ConfigureAwait(true);
        await using var clientLifetime = client.ConfigureAwait(true);
        List<int> statuses = [];
        ValueTask OnHeadAsync(Http3UpstreamResponseHead early, CancellationToken token)
        {
            Assert.Equal(deadline.Token, token);
            statuses.Add(early.StatusCode);
            if (callbackFailure) throw new IOException("Presentation callback failed.");
            return ValueTask.CompletedTask;
        }
        var peer = RespondAsync(fixture, "final", deadline.Token);
        try
        {
            await client.SendHeadersAsync([new(":method", "GET"), new(":scheme", "https"), new(":authority", "upstream.test"), new(":path", "/early")], true, timeouts, deadline.Token).ConfigureAwait(true);
            if (callbackFailure)
            {
                var error = await Assert.ThrowsAsync<IOException>(async () =>
                    await client.ReadResponseHeadAsync(4096, timeouts, OnHeadAsync, deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
                Assert.Equal("Presentation callback failed.", error.Message);
                Assert.Equal([102], statuses);
                Assert.Equal(Http3UpstreamPooledConnectionState.Failed, fixture.Connection.State);
            }
            else
            {
                var head = await client.ReadResponseHeadAsync(4096, timeouts, OnHeadAsync, deadline.Token).ConfigureAwait(true);
                Assert.Equal(200, head.StatusCode);
                Assert.Equal(EarlyStatuses, statuses);
                Assert.Equal("final"u8.ToArray(), (await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true)).Data);
                Assert.True((await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true)).EndStream);
            }
            await peer.ConfigureAwait(true);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or QuicException) { }
        }
    }

    private static async Task RespondAsync(DevelopmentOwnedQuicPeer fixture, string kind, CancellationToken cancellationToken)
    {
        var stream = await fixture.AcceptRequestAsync(cancellationToken).ConfigureAwait(false);
        await using var streamLifetime = stream.ConfigureAwait(false);
        Assert.Equal(QuicStreamType.Bidirectional, stream.Type);
        using var request = new MemoryStream();
        await stream.CopyToAsync(request, cancellationToken).ConfigureAwait(false);
        var offset = 0;
        Assert.True(Http3Codec.TryReadFrame(request.ToArray(), ref offset, out var type, out var payload));
        Assert.Equal(Http3Codec.HeadersFrame, type);
        Assert.True(Http3Codec.TryDecodeHeaderBlock(payload.Span, 4096, out var fields, out var reason), reason);
        Assert.Contains(fields, static field => field.Name.Equals(":path", StringComparison.Ordinal) && field.Value.Equals("/early", StringComparison.Ordinal));
        using var response = new MemoryStream();
        WriteEarlyHeads(response, kind);
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame,
            Http3Codec.EncodeHeaderBlock([new(":status", "200"), new("content-length", "5")]));
        Http3Codec.WriteFrame(response, Http3Codec.DataFrame, "final"u8);
        await stream.WriteAsync(response.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
    }

    private static void WriteEarlyHeads(Stream response, string kind)
    {
        if (kind is "order" or "uppercase" or "control" or "fields")
        {
            var fields = MalformedFields(kind);
            Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, DevelopmentRawQpack.EncodeHeaderBlock(fields));
            return;
        }
        if (string.Equals(kind, "upgrade", StringComparison.Ordinal))
            Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([new(":status", "101")]));
        else if (string.Equals(kind, "length", StringComparison.Ordinal))
            Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([new(":status", "103"), new("content-length", "0")]));
        else
        {
            var count = string.Equals(kind, "limit", StringComparison.Ordinal) ? 9 : 2;
            for (var index = 0; index < count; index++)
                Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame,
                    Http3Codec.EncodeHeaderBlock([new(":status", index == 0 ? "102" : "103"), new("link", "</style.css>; rel=preload")]));
        }
    }

    private static List<ProxyHeaderField> MalformedFields(string kind)
    {
        if (string.Equals(kind, "order", StringComparison.Ordinal)) return [new("x-early", "first"), new(":status", "103")];
        if (string.Equals(kind, "uppercase", StringComparison.Ordinal)) return [new(":status", "103"), new("X-Early", "value")];
        if (string.Equals(kind, "control", StringComparison.Ordinal)) return [new(":status", "103"), new("x-early", "unsafe\r\nfield")];
        List<ProxyHeaderField> fields = [new(":status", "103")];
        for (var index = 0; index < 128; index++) fields.Add(new("x-early", "value"));
        return fields;
    }
}
