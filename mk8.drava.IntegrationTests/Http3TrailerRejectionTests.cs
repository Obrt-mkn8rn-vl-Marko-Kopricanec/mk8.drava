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
public sealed class Http3TrailerRejectionTests
{
    [Theory]
    [InlineData("uppercase")]
    [InlineData("length")]
    [InlineData("pseudo")]
    [InlineData("control")]
    [InlineData("fields")]
    [InlineData("bytes")]
    [InlineData("data-after")]
    [InlineData("two-heads")]
    public async Task ActualQuicRejectsMalformedTrailersBeforeTheyReachTheExchangeAsync(string kind)
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
            await client.SendHeadersAsync([new(":method", "GET"), new(":scheme", "https"), new(":authority", "upstream.test"), new(":path", "/trailers")], true, timeouts, deadline.Token).ConfigureAwait(true);
            Assert.Equal(200, (await client.ReadResponseHeadAsync(1024, timeouts, deadline.Token).ConfigureAwait(true)).StatusCode);
            Assert.Equal("abc"u8.ToArray(), (await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true)).Data);
            await Assert.ThrowsAsync<Http3UpstreamProtocolException>(async () =>
                await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
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

    private static async Task RespondAsync(DevelopmentOwnedQuicPeer fixture, string kind, CancellationToken cancellationToken)
    {
        var stream = await fixture.AcceptRequestAsync(cancellationToken).ConfigureAwait(false);
        await using var streamLifetime = stream.ConfigureAwait(false);
        using var request = new MemoryStream();
        await stream.CopyToAsync(request, cancellationToken).ConfigureAwait(false);
        var offset = 0;
        Assert.True(Http3Codec.TryReadFrame(request.ToArray(), ref offset, out var type, out var payload));
        Assert.Equal(Http3Codec.HeadersFrame, type);
        Assert.True(Http3Codec.TryDecodeHeaderBlock(payload.Span, 4096, out var fields, out var reason), reason);
        Assert.Contains(fields, static field => field.Name.Equals(":path", StringComparison.Ordinal) && field.Value.Equals("/trailers", StringComparison.Ordinal));
        using var response = new MemoryStream();
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([new(":status", "200"), new("content-length", "3")]));
        Http3Codec.WriteFrame(response, Http3Codec.DataFrame, "abc"u8);
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, DevelopmentRawQpack.EncodeHeaderBlock(TrailerFields(kind)));
        if (string.Equals(kind, "data-after", StringComparison.Ordinal)) Http3Codec.WriteFrame(response, Http3Codec.DataFrame, "d"u8);
        if (string.Equals(kind, "two-heads", StringComparison.Ordinal))
            Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([new("x-end", "again")]));
        await stream.WriteAsync(response.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
    }

    private static List<ProxyHeaderField> TrailerFields(string kind)
    {
        if (string.Equals(kind, "uppercase", StringComparison.Ordinal)) return [new("X-End", "value")];
        if (string.Equals(kind, "length", StringComparison.Ordinal)) return [new("content-length", "3")];
        if (string.Equals(kind, "pseudo", StringComparison.Ordinal)) return [new(":status", "200")];
        if (string.Equals(kind, "control", StringComparison.Ordinal)) return [new("x-end", "unsafe\r\nfield")];
        if (string.Equals(kind, "bytes", StringComparison.Ordinal)) return [new("x-end", new string('x', 2048))];
        if (!string.Equals(kind, "fields", StringComparison.Ordinal)) return [new("x-end", "done")];
        List<ProxyHeaderField> fields = [];
        for (var index = 0; index < 129; index++) fields.Add(new("x-end", "value"));
        return fields;
    }
}
