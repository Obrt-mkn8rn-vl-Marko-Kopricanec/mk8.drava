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
public sealed class Http3TrailerPreservationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ActualQuicPreservesOpaqueOctetsAndEmptyTrailersAfterCompleteBodiesAsync(bool emptyBody, bool emptyTrailers)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var fixture = await DevelopmentOwnedQuicPeer.CreateAsync(processor: null, deadline.Token).ConfigureAwait(true);
        await using var fixtureLifetime = fixture.ConfigureAwait(true);
        Assert.True(fixture.Connection.TryReserveStream(TimeSpan.FromMinutes(1)));
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        var client = await Http3UpstreamConnection.OpenStreamAsync(fixture.Connection, timeouts, fixture.Metrics, 16384, deadline.Token).ConfigureAwait(true);
        await using var clientLifetime = client.ConfigureAwait(true);
        var peer = RespondAsync(fixture, emptyBody, emptyTrailers, deadline.Token);
        try
        {
            await client.SendHeadersAsync([new(":method", "GET"), new(":scheme", "https"), new(":authority", "upstream.test"), new(":path", "/trailers")], true, timeouts, deadline.Token).ConfigureAwait(true);
            Assert.Equal(200, (await client.ReadResponseHeadAsync(1024, timeouts, deadline.Token).ConfigureAwait(true)).StatusCode);
            if (!emptyBody) Assert.Equal("abc"u8.ToArray(), (await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true)).Data);
            var end = await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true);
            Assert.True(end.EndStream);
            Assert.Empty(end.Data);
            var fields = Assert.IsAssignableFrom<IReadOnlyList<ProxyHeaderField>>(end.Trailers);
            if (emptyTrailers) Assert.Empty(fields);
            else Assert.Collection(fields, static field => Assert.Equal(new ProxyHeaderField("x-end", "café"), field));
            Assert.True((await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true)).EndStream);
            await peer.ConfigureAwait(true);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or QuicException) { }
        }
    }

    private static async Task RespondAsync(DevelopmentOwnedQuicPeer fixture, bool emptyBody, bool emptyTrailers,
        CancellationToken cancellationToken)
    {
        var stream = await fixture.AcceptRequestAsync(cancellationToken).ConfigureAwait(false);
        await using var streamLifetime = stream.ConfigureAwait(false);
        using var request = new MemoryStream();
        await stream.CopyToAsync(request, cancellationToken).ConfigureAwait(false);
        using var response = new MemoryStream();
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([
            new(":status", "200"), new("content-length", emptyBody ? "0" : "3")]));
        if (!emptyBody) Http3Codec.WriteFrame(response, Http3Codec.DataFrame, "abc"u8);
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame,
            Http3Codec.EncodeHeaderBlock(emptyTrailers ? [] : [new("x-end", "café")]));
        await stream.WriteAsync(response.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
    }
}
