using System.Runtime.Versioning;
using System.Net.Quic;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
public sealed class Http3OwnedShutdownTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDisposalJoinsTheHeldPeerStreamBeforeClosingAsync(bool callbackFailure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        async Task ProcessAsync(QuicStream stream, CancellationToken cancellationToken)
        {
            await using var owned = stream.ConfigureAwait(false);
            using var callback = callbackFailure
                ? cancellationToken.Register(static () => throw new IOException("Owned cancellation callback failure."))
                : default;
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { stopping.TrySetResult(); }
            await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Interlocked.Increment(ref completed);
        }
        var fixture = await DevelopmentOwnedQuicPeer.CreateAsync(ProcessAsync, timeout.Token).ConfigureAwait(true);
        Task? first = null; Task? second = null;
        try
        {
            var peerStream = await fixture.OpenHeldControlAsync(timeout.Token).ConfigureAwait(true);
            await using var peerLifetime = peerStream.ConfigureAwait(true);
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(true);
            first = fixture.Connection.DisposeAsync().AsTask();
            await stopping.Task.WaitAsync(timeout.Token).ConfigureAwait(true);
            second = fixture.Connection.DisposeAsync().AsTask();
            Assert.Same(first, second); Assert.False(first.IsCompleted); Assert.Equal(0, Volatile.Read(ref completed));
            Assert.Equal(Http3UpstreamPooledConnectionState.ShutdownDisposing, fixture.Connection.State);
            Assert.Equal(0, fixture.Metrics.Snapshot().UpstreamHttp3.PoolConnectionsClosed);
            release.TrySetResult();
            await JoinAsync(Task.WhenAll(first, second), callbackFailure, timeout.Token).ConfigureAwait(true);
            Assert.Equal(1, Volatile.Read(ref completed));
            await AssertClosedOnceAsync(fixture, callbackFailure, timeout.Token).ConfigureAwait(true);
        }
        finally
        {
            release.TrySetResult();
            try
            {
                if (first is not null) await JoinAsync(first, callbackFailure, timeout.Token).ConfigureAwait(true);
                if (second is not null) await JoinAsync(second, callbackFailure, timeout.Token).ConfigureAwait(true);
            }
            finally { await JoinAsync(fixture.DisposeAsync().AsTask(), callbackFailure, timeout.Token).ConfigureAwait(true); }
        }
    }

    private static async Task JoinAsync(Task disposal, bool callbackFailure, CancellationToken cancellationToken)
    {
        if (!callbackFailure)
        {
            await disposal.WaitAsync(cancellationToken).ConfigureAwait(true);
            return;
        }
        var exception = await Assert.ThrowsAsync<AggregateException>(() => disposal.WaitAsync(cancellationToken)).ConfigureAwait(true);
        Assert.All(exception.Flatten().InnerExceptions, static inner =>
            Assert.Equal("Owned cancellation callback failure.", Assert.IsType<IOException>(inner).Message));
    }

    private static async Task AssertClosedOnceAsync(DevelopmentOwnedQuicPeer fixture, bool callbackFailure, CancellationToken cancellationToken)
    {
        Assert.Equal(Http3UpstreamPooledConnectionState.Closed, fixture.Connection.State);
        Assert.Equal(1, fixture.Metrics.Snapshot().UpstreamHttp3.PoolConnectionsClosed);
        Assert.Equal(0, fixture.Metrics.Snapshot().UpstreamHttp3.ActiveConnections);
        fixture.Connection.ReleaseStream(connectionUsable: false); fixture.Connection.MarkUnusable();
        Assert.Equal(Http3UpstreamPooledConnectionState.Closed, fixture.Connection.State);
        await JoinAsync(fixture.Connection.DisposeAsync().AsTask(), callbackFailure, cancellationToken).ConfigureAwait(true);
        Assert.Equal(1, fixture.Metrics.Snapshot().UpstreamHttp3.PoolConnectionsClosed);
    }

    [Fact]
    public async Task DefaultPeerControlReadIsCanceledAndJoinedOnDisposalAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var fixture = await DevelopmentOwnedQuicPeer.CreateAsync(processor: null, timeout.Token).ConfigureAwait(true);
        await using var fixtureLifetime = fixture.ConfigureAwait(true);
        var peerStream = await fixture.OpenHeldControlAsync(timeout.Token).ConfigureAwait(true);
        await using var peerLifetime = peerStream.ConfigureAwait(true);
        await DevelopmentOwnedQuicPeer.SendGoAwayAsync(peerStream, timeout.Token).ConfigureAwait(true);
        await WaitForDrainingAsync(fixture.Connection, timeout.Token).ConfigureAwait(true);
        Assert.False(fixture.Connection.TryReserveStream(TimeSpan.FromMinutes(1)));
        await fixture.Connection.DisposeAsync().AsTask().WaitAsync(timeout.Token).ConfigureAwait(true);
        Assert.Equal(Http3UpstreamPooledConnectionState.Closed, fixture.Connection.State);
        Assert.Equal(1, fixture.Metrics.Snapshot().UpstreamHttp3.PoolConnectionsClosed);
        Assert.Equal(0, fixture.Metrics.Snapshot().UpstreamHttp3.ActiveConnections);
    }
    private static async Task WaitForDrainingAsync(Http3UpstreamPooledConnection connection, CancellationToken cancellationToken)
    {
        while (connection.State != Http3UpstreamPooledConnectionState.Draining)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(true);
        }
    }

}
