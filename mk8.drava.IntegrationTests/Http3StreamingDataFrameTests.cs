using System.Net.Quic;
using System.Runtime.Versioning;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
public sealed class Http3StreamingDataFrameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualQuicLargeDataFrameUsesBoundedChunksAndTruncationRetiresTheLeaseAsync(bool truncated)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var fixture = await DevelopmentOwnedQuicPeer.CreateAsync(processor: null, deadline.Token).ConfigureAwait(true);
        await using var fixtureLifetime = fixture.ConfigureAwait(true);
        Assert.True(fixture.Connection.TryReserveStream(TimeSpan.FromMinutes(1)));
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        var client = await Http3UpstreamConnection.OpenStreamAsync(fixture.Connection, timeouts, fixture.Metrics, 16384, deadline.Token).ConfigureAwait(true);
        await using var clientLifetime = client.ConfigureAwait(true);
        var body = new byte[65537];
        for (var index = 0; index < body.Length; index++) body[index] = (byte)(index % 251);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = RespondAsync(fixture, body, truncated, finished, deadline.Token);
        try
        {
            await client.SendHeadersAsync([new(":method", "GET"), new(":scheme", "https"), new(":authority", "upstream.test"),
                new(":path", "/large")], true, timeouts, deadline.Token).ConfigureAwait(true);
            Assert.Equal(200, (await client.ReadResponseHeadAsync(4096, timeouts, deadline.Token).ConfigureAwait(true)).StatusCode);
            if (truncated)
            {
                var error = await Assert.ThrowsAsync<Http3UpstreamProtocolException>(async () =>
                    await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
                Assert.Contains("closed mid", error.Message, StringComparison.Ordinal);
                Assert.Equal(Http3UpstreamPooledConnectionState.Failed, fixture.Connection.State);
            }
            else
                await VerifyChunksAsync(client, body, timeouts, deadline.Token).ConfigureAwait(true);
            finished.TrySetResult();
            await peer.ConfigureAwait(true);
        }
        finally
        {
            finished.TrySetResult();
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or QuicException) { }
        }
    }

    private static async Task VerifyChunksAsync(Http3UpstreamConnection client, byte[] expected,
        RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        var chunks = 0;
        Http3UpstreamDataChunk data;
        do
        {
            data = await client.ReadDataAsync(timeouts, cancellationToken).ConfigureAwait(false);
            Assert.InRange(data.Data.Length, 0, 16384);
            if (data.Data.Length > 0) chunks++;
            body.Write(data.Data);
        } while (!data.EndStream);
        Assert.Equal(5, chunks);
        Assert.Equal(expected, body.ToArray());
        Assert.Collection(Assert.IsAssignableFrom<IReadOnlyList<Mk8.Drava.Application.BLL.Http.ProxyHeaderField>>(data.Trailers),
            field => { Assert.Equal("x-end", field.Name); Assert.Equal("complete", field.Value); });
        Assert.True((await client.ReadDataAsync(timeouts, cancellationToken).ConfigureAwait(false)).EndStream);
    }

    private static async Task RespondAsync(DevelopmentOwnedQuicPeer fixture, byte[] body, bool truncated,
        TaskCompletionSource finished, CancellationToken cancellationToken)
    {
        var stream = await fixture.AcceptRequestAsync(cancellationToken).ConfigureAwait(false);
        await using var streamLifetime = stream.ConfigureAwait(false);
        using var request = new MemoryStream();
        await stream.CopyToAsync(request, cancellationToken).ConfigureAwait(false);
        using var response = new MemoryStream();
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([new(":status", "200")]));
        Http3Codec.WriteVarInt(response, Http3Codec.DataFrame);
        Http3Codec.WriteVarInt(response, body.Length);
        await response.WriteAsync(body.AsMemory(0, truncated ? 1024 : body.Length), cancellationToken).ConfigureAwait(false);
        if (!truncated) Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame,
            Http3Codec.EncodeHeaderBlock([new("x-end", "complete")]));
        await stream.WriteAsync(response.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
        await finished.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
