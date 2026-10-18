using Mk8.Drava.Application.INF.NodeRelay;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RelayConnectionCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectionCleanupJoinsTheStreamAndAttemptsEveryResourceDespiteFailuresAsync(bool multipleFailures)
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var streamFailure = new InvalidOperationException("Controlled stream disposal failure.");
        var resourceFailure = new IOException("Controlled transport disposal failure.");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = new CleanupResource(multipleFailures ? resourceFailure : null);
        using var last = new CleanupResource(failure: null);
        var stream = new CleanupStream(release.Task, streamFailure, guard.Token);
        await using var streamLifetime = stream.ConfigureAwait(true);
        var cleanup = RelayConnectionCleanup.DisposeAsync(stream, first, null, last).AsTask();
        try
        {
            Assert.False(cleanup.IsCompleted);
            Assert.Equal(0, first.Calls);
            Assert.Equal(0, last.Calls);
            release.TrySetResult();
            if (multipleFailures)
            {
                var failure = await Assert.ThrowsAsync<AggregateException>(() => cleanup.WaitAsync(guard.Token)).ConfigureAwait(true);
                Assert.Equal(new Exception[] { streamFailure, resourceFailure }, failure.InnerExceptions);
            }
            else
            {
                Assert.Same(streamFailure, await Assert.ThrowsAsync<InvalidOperationException>(() => cleanup.WaitAsync(guard.Token)).ConfigureAwait(true));
            }
            Assert.Equal(1, first.Calls);
            Assert.Equal(1, last.Calls);
        }
        finally
        {
            release.TrySetResult();
            try { await cleanup.WaitAsync(guard.Token).ConfigureAwait(true); }
            catch (Exception exception) when (exception is AggregateException or InvalidOperationException) { }
        }
    }

    private sealed class CleanupStream(Task release, Exception failure, CancellationToken guard) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            await release.WaitAsync(guard).ConfigureAwait(false);
            throw failure;
        }
    }

    private sealed class CleanupResource(Exception? failure) : IDisposable
    {
        private int _disposed;
        public int Calls => Volatile.Read(ref _disposed);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && failure is not null) throw failure;
        }
    }
}
