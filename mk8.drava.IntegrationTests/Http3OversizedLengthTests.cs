using System.Net.Quic;
using System.Runtime.Versioning;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
public sealed class Http3OversizedLengthTests
{
    [Theory]
    [InlineData(0x100000000)]
    [InlineData(0x3fffffffffffffff)]
    public async Task ActualQuicRejectsOversizedFrameLengthsBeforeIntegerWrapOrAllocationAsync(long length)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var fixture = await DevelopmentOwnedQuicPeer.CreateAsync(processor: null, deadline.Token).ConfigureAwait(true);
        await using var fixtureLifetime = fixture.ConfigureAwait(true);
        Assert.True(fixture.Connection.TryReserveStream(TimeSpan.FromMinutes(1)));
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        var client = await Http3UpstreamConnection.OpenStreamAsync(fixture.Connection, timeouts, fixture.Metrics, 16384, deadline.Token).ConfigureAwait(true);
        await using var clientLifetime = client.ConfigureAwait(true);
        var peer = RespondAsync(fixture, length, deadline.Token);
        try
        {
            await client.SendHeadersAsync([new(":method", "GET"), new(":scheme", "https"), new(":authority", "upstream.test"), new(":path", "/oversized")], true, timeouts, deadline.Token).ConfigureAwait(true);
            Assert.Equal(200, (await client.ReadResponseHeadAsync(1024, timeouts, deadline.Token).ConfigureAwait(true)).StatusCode);
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

    private static async Task RespondAsync(DevelopmentOwnedQuicPeer fixture, long length, CancellationToken cancellationToken)
    {
        var stream = await fixture.AcceptRequestAsync(cancellationToken).ConfigureAwait(false);
        await using var streamLifetime = stream.ConfigureAwait(false);
        using var request = new MemoryStream();
        await stream.CopyToAsync(request, cancellationToken).ConfigureAwait(false);
        using var response = new MemoryStream();
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([new(":status", "200")]));
        Http3Codec.WriteVarInt(response, Http3Codec.DataFrame);
        Http3Codec.WriteVarInt(response, length);
        await stream.WriteAsync(response.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
    }
}
