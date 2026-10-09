using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class Http2OwnedShutdownTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDisposalJoinsTheCanceledSocketReadAndClosesItsOwnedStreamOnceAsync(bool callbackFailure)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var stream = new HeldCancellationReadStream(pair.Client, callbackFailure, deadline.Token);
        var connection = new Http2UpstreamConnection(stream, new ProxyMetrics(), leaveOpen: false);
        Task? first = null;
        Task? second = null;
        try
        {
            await pair.InitializeAsync(connection).ConfigureAwait(true);
            first = connection.DisposeAsync().AsTask();
            await stream.Stopping.WaitAsync(deadline.Token).ConfigureAwait(true);
            second = connection.DisposeAsync().AsTask();
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal(0, stream.DisposeCount);
            stream.ReleaseRead();
            await JoinAsync(first, callbackFailure, deadline.Token).ConfigureAwait(true);
            await JoinAsync(second, callbackFailure, deadline.Token).ConfigureAwait(true);
            Assert.True(stream.CanceledReadCompleted.IsCompletedSuccessfully);
            Assert.Equal(1, stream.DisposeCount);
            await JoinAsync(connection.DisposeAsync().AsTask(), callbackFailure, deadline.Token).ConfigureAwait(true);
            Assert.Equal(1, stream.DisposeCount);
        }
        finally
        {
            stream.ReleaseRead();
            try
            {
                if (first is not null) await ObserveCleanupAsync(first, callbackFailure, deadline.Token).ConfigureAwait(true);
                if (second is not null) await ObserveCleanupAsync(second, callbackFailure, deadline.Token).ConfigureAwait(true);
                await ObserveCleanupAsync(connection.DisposeAsync().AsTask(), callbackFailure, deadline.Token).ConfigureAwait(true);
            }
            finally
            {
                try
                {
                    if (stream.Stopping.IsCompletedSuccessfully)
                        await stream.CanceledReadCompleted.WaitAsync(deadline.Token).ConfigureAwait(true);
                }
                finally { await stream.DisposeAsync().ConfigureAwait(true); }
            }
        }
    }

    private static async Task JoinAsync(Task disposal, bool callbackFailure, CancellationToken cancellationToken)
    {
        if (!callbackFailure)
        {
            await disposal.WaitAsync(cancellationToken).ConfigureAwait(true);
            return;
        }
        var error = await Assert.ThrowsAsync<AggregateException>(() => disposal.WaitAsync(cancellationToken)).ConfigureAwait(true);
        Assert.All(error.Flatten().InnerExceptions, static exception =>
            Assert.Equal("Owned cancellation callback failure.", Assert.IsType<IOException>(exception).Message));
    }

    private static async Task ObserveCleanupAsync(Task disposal, bool callbackFailure, CancellationToken cancellationToken)
    {
        try { await disposal.WaitAsync(cancellationToken).ConfigureAwait(true); }
        catch (AggregateException error) when (callbackFailure)
        {
            Assert.All(error.Flatten().InnerExceptions, static exception =>
                Assert.Equal("Owned cancellation callback failure.", Assert.IsType<IOException>(exception).Message));
        }
    }
}
