using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Hosting;

namespace Mk8.Drava.CompatibilityTests;

internal static class NativeListenerLifetimeTests
{
    public static async Task StopJoinsAdmittedReloadsAndRefusesNewOnesAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var calls = 0;
        var closes = 0;
        var lifetime = new NativeListenerLifetime(() => { calls++; return Task.CompletedTask; }, () => closes++);
        NativeListenerLifetime.ReloadLease? first = null;
        NativeListenerLifetime.ReloadLease? second = null;
        Task<NativeListenerLifetime.ReloadLease>? pending = null;
        try
        {
            first = await lifetime.EnterReloadAsync(guard.Token).ConfigureAwait(false);
            pending = lifetime.EnterReloadAsync(guard.Token).AsTask();
            var stop = lifetime.StopAsync(CancellationToken.None);
            AssertEx.True(lifetime.StoppingToken.IsCancellationRequested);
            AssertEx.False(stop.IsCompleted);
            AssertEx.Equal(0, calls);
            await AssertEx.ThrowsAsync<ObjectDisposedException>(async () =>
            {
                using var unexpected = await lifetime.EnterReloadAsync(guard.Token).ConfigureAwait(false);
            }).ConfigureAwait(false);
            first.Dispose();
            second = await pending.WaitAsync(guard.Token).ConfigureAwait(false);
            AssertEx.False(stop.IsCompleted);
            second.Dispose();
            await stop.WaitAsync(guard.Token).ConfigureAwait(false);
            await lifetime.DisposeAsync().AsTask().WaitAsync(guard.Token).ConfigureAwait(false);
            AssertEx.Equal(1, calls);
            AssertEx.Equal(1, closes);
        }
        finally
        {
            first?.Dispose();
            try
            {
                if (second is null && pending is not null)
                {
                    try { second = await pending.WaitAsync(guard.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (pending.IsCanceled) { }
                }
            }
            finally
            {
                second?.Dispose();
                await lifetime.DisposeAsync().AsTask().WaitAsync(guard.Token).ConfigureAwait(false);
            }
        }
    }

    public static async Task CanceledReloadWaitDoesNotRetainAdmissionAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancel = new CancellationTokenSource();
        var calls = 0;
        var lifetime = new NativeListenerLifetime(() => { calls++; return Task.CompletedTask; }, static () => { });
        using var first = await lifetime.EnterReloadAsync(guard.Token).ConfigureAwait(false);
        var pending = lifetime.EnterReloadAsync(cancel.Token).AsTask();
        try
        {
            await cancel.CancelAsync().ConfigureAwait(false);
            await AssertEx.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(guard.Token).ConfigureAwait(false)).ConfigureAwait(false);
            var stop = lifetime.StopAsync(CancellationToken.None);
            AssertEx.False(stop.IsCompleted);
            first.Dispose();
            await stop.WaitAsync(guard.Token).ConfigureAwait(false);
            AssertEx.Equal(1, calls);
        }
        finally
        {
            await cancel.CancelAsync().ConfigureAwait(false);
            try
            {
                try { await pending.WaitAsync(guard.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (pending.IsCanceled) { }
            }
            finally
            {
                first.Dispose();
                await lifetime.DisposeAsync().AsTask().WaitAsync(guard.Token).ConfigureAwait(false);
            }
        }
    }

    public static async Task CallerCancellationDoesNotCancelSharedStopOrDisposalAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var caller = new CancellationTokenSource();
        var entered = Signal();
        var release = Signal();
        var calls = 0;
        var closes = 0;
        var lifetime = new NativeListenerLifetime(async () =>
        {
            calls++;
            entered.TrySetResult();
            await release.Task.WaitAsync(guard.Token).ConfigureAwait(false);
        }, () => closes++);
        try
        {
            var waiting = lifetime.StopAsync(caller.Token);
            await entered.Task.WaitAsync(guard.Token).ConfigureAwait(false);
            var owned = lifetime.StopAsync(CancellationToken.None);
            AssertEx.True(ReferenceEquals(owned, lifetime.StopAsync(CancellationToken.None)));
            await caller.CancelAsync().ConfigureAwait(false);
            await AssertEx.ThrowsAsync<OperationCanceledException>(async () => await waiting.WaitAsync(guard.Token).ConfigureAwait(false)).ConfigureAwait(false);
            AssertEx.False(owned.IsCompleted);
            var first = lifetime.DisposeAsync().AsTask();
            var second = lifetime.DisposeAsync().AsTask();
            AssertEx.True(ReferenceEquals(first, second));
            AssertEx.False(first.IsCompleted);
            AssertEx.Equal(0, closes);
            release.TrySetResult();
            await first.WaitAsync(guard.Token).ConfigureAwait(false);
            await owned.WaitAsync(guard.Token).ConfigureAwait(false);
            AssertEx.Equal(1, calls);
            AssertEx.Equal(1, closes);
            AssertEx.Throws<ObjectDisposedException>(() => lifetime.StoppingToken);
        }
        finally
        {
            release.TrySetResult();
            await lifetime.DisposeAsync().AsTask().WaitAsync(guard.Token).ConfigureAwait(false);
        }
    }

    public static async Task StopFailureIsRetainedByRepeatedStopAndDisposalAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var release = Signal();
        var failure = new IOException("Controlled settled listener stop failure.");
        var calls = 0;
        var closes = 0;
        var lifetime = new NativeListenerLifetime(async () =>
        {
            calls++;
            await release.Task.WaitAsync(guard.Token).ConfigureAwait(false);
            throw failure;
        }, () => closes++);
        try
        {
            var stop = lifetime.StopAsync(CancellationToken.None);
            var dispose = lifetime.DisposeAsync().AsTask();
            AssertEx.False(dispose.IsCompleted);
            AssertEx.Equal(0, closes);
            release.TrySetResult();
            await ObserveExpectedFailureAsync(stop, failure, guard.Token).ConfigureAwait(false);
            await ObserveExpectedFailureAsync(dispose, failure, guard.Token).ConfigureAwait(false);
            await ObserveExpectedFailureAsync(lifetime.DisposeAsync().AsTask(), failure, guard.Token).ConfigureAwait(false);
            AssertEx.Equal(1, calls);
            AssertEx.Equal(1, closes);
        }
        finally
        {
            release.TrySetResult();
            await ObserveExpectedFailureAsync(lifetime.DisposeAsync().AsTask(), failure, guard.Token).ConfigureAwait(false);
        }
    }

    public static async Task ThrowingCancellationCallbackStillJoinsReloadAndStopWorkAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var entered = Signal();
        var release = Signal();
        var callback = Signal();
        var failure = new IOException("Controlled listener cancellation callback failure.");
        var closes = 0;
        var lifetime = new NativeListenerLifetime(async () =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(guard.Token).ConfigureAwait(false);
        }, () => closes++);
        await using var registration = lifetime.StoppingToken.Register(() => { callback.TrySetResult(); throw failure; }).ConfigureAwait(false);
        using var lease = await lifetime.EnterReloadAsync(guard.Token).ConfigureAwait(false);
        try
        {
            var stop = lifetime.StopAsync(CancellationToken.None);
            await callback.Task.WaitAsync(guard.Token).ConfigureAwait(false);
            AssertEx.False(entered.Task.IsCompleted);
            AssertEx.False(stop.IsCompleted);
            lease.Dispose();
            await entered.Task.WaitAsync(guard.Token).ConfigureAwait(false);
            var dispose = lifetime.DisposeAsync().AsTask();
            AssertEx.False(dispose.IsCompleted);
            AssertEx.Equal(0, closes);
            release.TrySetResult();
            await ObserveExpectedAggregateAsync(stop, failure, guard.Token).ConfigureAwait(false);
            await ObserveExpectedAggregateAsync(dispose, failure, guard.Token).ConfigureAwait(false);
            AssertEx.Equal(1, closes);
        }
        finally
        {
            lease.Dispose();
            release.TrySetResult();
            await ObserveExpectedAggregateAsync(lifetime.DisposeAsync().AsTask(), failure, guard.Token).ConfigureAwait(false);
        }
    }

    public static async Task SynchronousDisposeNeverBlocksUnsettledWorkAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var release = Signal();
        var closes = 0;
        var lifetime = new NativeListenerLifetime(() => release.Task.WaitAsync(guard.Token), () => closes++);
        try
        {
            AssertEx.Throws<InvalidOperationException>(lifetime.Dispose);
            AssertEx.Equal(0, closes);
            var disposal = lifetime.DisposeAsync().AsTask();
            AssertEx.False(disposal.IsCompleted);
            release.TrySetResult();
            await disposal.WaitAsync(guard.Token).ConfigureAwait(false);
            #pragma warning disable CA1849, VSTHRD103, MA0042 // This regression deliberately verifies the synchronous adapter after its shared disposal task has successfully settled.
            lifetime.Dispose();
            #pragma warning restore CA1849, VSTHRD103, MA0042
            AssertEx.Equal(1, closes);
        }
        finally
        {
            release.TrySetResult();
            await lifetime.DisposeAsync().AsTask().WaitAsync(guard.Token).ConfigureAwait(false);
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task ObserveExpectedFailureAsync(Task operation, IOException expected, CancellationToken cancellationToken)
    {
        try { await operation.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (IOException exception) when (ReferenceEquals(exception, expected)) { return; }
        throw new InvalidOperationException("Expected the retained controlled stop failure.");
    }

    private static async Task ObserveExpectedAggregateAsync(Task operation, IOException expected, CancellationToken cancellationToken)
    {
        try { await operation.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (AggregateException exception)
        {
            AssertEx.True(exception.Flatten().InnerExceptions.All(error => ReferenceEquals(error, expected)));
            return;
        }
        throw new InvalidOperationException("Expected the retained controlled callback failure.");
    }
}
