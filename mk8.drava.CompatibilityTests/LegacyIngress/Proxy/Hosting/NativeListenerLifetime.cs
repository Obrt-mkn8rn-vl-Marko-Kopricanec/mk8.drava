using System.Runtime.ExceptionServices;

namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Hosting;

// Owns technical reload admission and shutdown settlement; business policy stays in the existing BLL.
internal sealed class NativeListenerLifetime(Func<Task> stopListeners, Action disposeBase) : IDisposable, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly CancellationTokenSource _serviceStopping = new();
    private readonly TaskCompletionSource _reloadsFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _stopCompletion;
    private TaskCompletionSource? _disposeCompletion;
    private int _reloads;
    private bool _disposed;

    public CancellationToken StoppingToken
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _serviceStopping.Token;
            }
        }
    }

    public async ValueTask<ReloadLease> EnterReloadAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopCompletion is not null || _disposeCompletion is not null, this);
            _reloads++;
        }
        try
        {
            var lease = new ReloadLease(this);
            await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return lease;
        }
        catch
        {
            FinishReload(releaseGate: false);
            throw;
        }
    }

    private void FinishReload(bool releaseGate)
    {
        if (releaseGate) _reloadGate.Release();
        lock (_gate)
        {
            _reloads--;
            if (_reloads == 0 && _stopCompletion is not null) _reloadsFinished.TrySetResult();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource completion;
        bool start;
        lock (_gate)
        {
            start = _stopCompletion is null;
            completion = _stopCompletion ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_reloads == 0) _reloadsFinished.TrySetResult();
        }
        if (start) _ = CompleteStopAsync(completion);
        return cancellationToken.CanBeCanceled ? completion.Task.WaitAsync(cancellationToken) : completion.Task;
    }

    private async Task CompleteStopAsync(TaskCompletionSource completion)
    {
        try
        {
            Exception? failure = null;
            try { await _serviceStopping.CancelAsync().ConfigureAwait(false); }
            #pragma warning disable CA1031 // Retain cancellation failure and still join all admitted users and listener cleanup.
            catch (Exception exception) { failure = exception; }
            #pragma warning restore CA1031
            #pragma warning disable VSTHRD003 // This private asynchronous signal is completed by this owner's admitted reload leases; no external context is required.
            await _reloadsFinished.Task.ConfigureAwait(false);
            #pragma warning restore VSTHRD003
            try { await stopListeners().ConfigureAwait(false); }
            #pragma warning disable CA1031 // Callback failure is returned through the shared stop promise after owned work settles.
            catch (Exception exception) { failure = Combine(failure, exception); }
            #pragma warning restore CA1031
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            completion.TrySetResult();
        }
        #pragma warning disable CA1031 // Promise bridge forwards the exact terminal failure to every joining caller.
        catch (Exception exception) { completion.TrySetException(exception); }
        #pragma warning restore CA1031
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        bool start;
        lock (_gate)
        {
            start = _disposeCompletion is null;
            completion = _disposeCompletion ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        if (start) _ = CompleteDisposalAsync(completion);
        return new ValueTask(completion.Task);
    }

    private async Task CompleteDisposalAsync(TaskCompletionSource completion)
    {
        Exception? failure = null;
        try { await StopAsync(CancellationToken.None).ConfigureAwait(false); }
        #pragma warning disable CA1031 // Retain the settled stop failure while releasing the now-unused technical resources.
        catch (Exception exception) { failure = exception; }
        #pragma warning restore CA1031
        lock (_gate) _disposed = true;
        try
        {
            _serviceStopping.Dispose();
            _reloadGate.Dispose();
            disposeBase();
        }
        #pragma warning disable CA1031 // Disposal errors remain observable through the shared disposal promise.
        catch (Exception exception) { failure = Combine(failure, exception); }
        #pragma warning restore CA1031
        if (failure is null) completion.TrySetResult();
        else completion.TrySetException(failure);
    }

    private static Exception Combine(Exception? first, Exception second) => first is null ? second : new AggregateException(first, second);

    public void Dispose()
    {
        var disposal = DisposeAsync();
        if (!disposal.IsCompleted) throw new InvalidOperationException("Listener disposal is pending; await DisposeAsync to join its completion.");
        #pragma warning disable VSTHRD002 // This synchronous adapter observes only an already-completed ValueTask; it never blocks pending work.
        disposal.GetAwaiter().GetResult();
        #pragma warning restore VSTHRD002
    }

    internal sealed class ReloadLease(NativeListenerLifetime owner) : IDisposable
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213", Justification = "The lifetime owner is borrowed. A lease releases only its own reload admission; stopping or disposing the shared owner here would invalidate other live leases. The service/host joins and disposes the owner separately.")]
        private NativeListenerLifetime? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, value: null)?.FinishReload(releaseGate: true);
    }
}
