using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class Http2InformationalLengthTests
{
    [Fact]
    public async Task InformationalContentLengthIsRejectedBeforeAFinalResponseAsync()
    {
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        await pair.InitializeAsync(connection).ConfigureAwait(true);
        await pair.RequestAsync(connection).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, Http2TestFlags.EndHeaders, 1,
            HpackCodec.EncodeResponseHeaders(100, [new("content-length", "0")]), pair.Deadline.Token).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, 5, 1,
            HpackCodec.EncodeResponseHeaders(200, []), pair.Deadline.Token).ConfigureAwait(true);
        await Assert.ThrowsAsync<Http2UpstreamProtocolException>(async () =>
            await connection.ReadResponseHeadAsync(4096, timeouts, pair.Deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }
}
