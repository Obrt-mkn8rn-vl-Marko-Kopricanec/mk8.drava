using System.Net.Quic;
using System.Runtime.Versioning;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
public sealed class Http3UploadDeadlineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualQuicResponseDeadlineStartsAfterRequestFinAndStillBoundsASilentPeerAsync(bool silent)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var fixture = await DevelopmentOwnedQuicPeer.CreateAsync(processor: null, deadline.Token).ConfigureAwait(true);
        await using var fixtureLifetime = fixture.ConfigureAwait(true);
        Assert.True(fixture.Connection.TryReserveStream(TimeSpan.FromMinutes(1)));
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromMilliseconds(750));
        var client = await Http3UpstreamConnection.OpenStreamAsync(fixture.Connection, timeouts, fixture.Metrics, 16384, deadline.Token).ConfigureAwait(true);
        await using var clientLifetime = client.ConfigureAwait(true);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = RespondAfterUploadAsync(fixture, silent, received, finished, deadline.Token);
        Task<Http3UpstreamResponseHead>? head = null;
        try
        {
            await client.SendHeadersAsync([new(":method", "POST"), new(":scheme", "https"), new(":authority", "upstream.test"),
                new(":path", "/slow"), new("content-length", "3")], false, timeouts, deadline.Token).ConfigureAwait(true);
            head = client.ReadResponseHeadAsync(4096, timeouts, deadline.Token).AsTask();
            await client.SendDataAsync("a"u8.ToArray(), false, timeouts, deadline.Token).ConfigureAwait(true);
            await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token).ConfigureAwait(true);
            await client.SendDataAsync("b"u8.ToArray(), false, timeouts, deadline.Token).ConfigureAwait(true);
            await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token).ConfigureAwait(true);
            Assert.False(head.IsCompleted, "An active upload must not consume the response-head deadline before request FIN.");
            await client.SendDataAsync("c"u8.ToArray(), true, timeouts, deadline.Token).ConfigureAwait(true);
            await received.Task.WaitAsync(deadline.Token).ConfigureAwait(true);
            if (silent)
            {
                var error = await Assert.ThrowsAsync<ProxyTimeoutException>(async () =>
                    await head.WaitAsync(TimeSpan.FromSeconds(5), deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
                Assert.Equal(ProxyTimeoutKind.UpstreamResponseHead, error.Kind);
                Assert.Equal(Http3UpstreamPooledConnectionState.Failed, fixture.Connection.State);
            }
            else
            {
                Assert.Equal(200, (await head.WaitAsync(TimeSpan.FromSeconds(5), deadline.Token).ConfigureAwait(true)).StatusCode);
                Assert.True((await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true)).EndStream);
            }
            finished.TrySetResult();
            await peer.ConfigureAwait(true);
        }
        finally
        {
            finished.TrySetResult();
            await deadline.CancelAsync().ConfigureAwait(true);
            if (head is not null)
            {
                try { await head.ConfigureAwait(true); }
                catch (Exception exception) when (exception is OperationCanceledException or IOException or QuicException or ProxyTimeoutException) { }
            }
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or QuicException) { }
        }
    }

    private static async Task RespondAfterUploadAsync(DevelopmentOwnedQuicPeer fixture, bool silent,
        TaskCompletionSource received, TaskCompletionSource finished, CancellationToken cancellationToken)
    {
        var stream = await fixture.AcceptRequestAsync(cancellationToken).ConfigureAwait(false);
        await using var streamLifetime = stream.ConfigureAwait(false);
        using var request = new MemoryStream();
        await stream.CopyToAsync(request, cancellationToken).ConfigureAwait(false);
        var bytes = request.ToArray();
        var offset = 0;
        Assert.True(Http3Codec.TryReadFrame(bytes, ref offset, out var type, out var payload));
        Assert.Equal(Http3Codec.HeadersFrame, type);
        using var body = new MemoryStream();
        while (offset < bytes.Length)
        {
            Assert.True(Http3Codec.TryReadFrame(bytes, ref offset, out type, out payload));
            Assert.Equal(Http3Codec.DataFrame, type);
            body.Write(payload.Span);
        }
        Assert.Equal("abc"u8.ToArray(), body.ToArray());
        received.TrySetResult();
        if (!silent)
        {
            using var response = new MemoryStream();
            Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame,
                Http3Codec.EncodeHeaderBlock([new(":status", "200"), new("content-length", "0")]));
            await stream.WriteAsync(response.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
        }
        await finished.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
