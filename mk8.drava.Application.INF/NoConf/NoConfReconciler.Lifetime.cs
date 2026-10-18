namespace Mk8.Drava.Application.INF.NoConf;

public sealed partial class NoConfReconciler
{
    private readonly Lock _lifetimeGate = new();
    private readonly SemaphoreSlim _operationsDrained = new(0, 1);
    private int _admittedOperations;
    private int _resourcesDisposed;
    private bool _stopping;
    private bool _drainSignaled;
    private Task? _stopTask;

    private void ThrowIfStopping()
    {
        lock (_lifetimeGate) ObjectDisposedException.ThrowIf(_stopping, this);
    }

    private Task StartExecutionAsync(CancellationToken cancellationToken)
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            return base.StartAsync(cancellationToken);
        }
    }

    private async ValueTask EnterCompilationAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(!await TryEnterCompilationAsync(cancellationToken).ConfigureAwait(false), this);
    }

    private async ValueTask<bool> TryEnterCompilationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lifetimeGate)
        {
            if (_stopping) return false;
            // Count admission before waiting, including administrative callers outside ExecuteAsync.
            _admittedOperations++;
        }
        try { await _compilationGate.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch { CompleteOperation(); throw; }
        return true;
    }

    private void ExitCompilation()
    {
        _compilationGate.Release();
        CompleteOperation();
    }

    private void CompleteOperation()
    {
        lock (_lifetimeGate)
        {
            _admittedOperations--;
            SignalDrained();
        }
    }

    private void SignalDrained()
    {
        if (!_stopping || _admittedOperations != 0 || _drainSignaled) return;
        _drainSignaled = true;
        _operationsDrained.Release();
    }

    private Task BeginStoppingAsync()
    {
        lock (_lifetimeGate)
        {
            _stopping = true;
            SignalDrained();
            // Publish the shared owner before cancellation callbacks can reenter shutdown.
            return _stopTask ??= Task.Run(StopOwnedAsync, CancellationToken.None);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken) => BeginStoppingAsync().WaitAsync(cancellationToken);

    private async Task StopOwnedAsync()
    {
        try
        {
            await Task.WhenAll(base.StopAsync(CancellationToken.None), JoinExecutionAsync(), _operationsDrained.WaitAsync(CancellationToken.None)).ConfigureAwait(false);
        }
        finally { DisposeResources(); }
    }

    private async Task JoinExecutionAsync()
    {
        if (ExecuteTask is not { } execution) return;
        try { await execution.ConfigureAwait(false); }
        catch (OperationCanceledException) when (execution.IsCanceled) { }
    }

    public override void Dispose()
    {
        _ = BeginStoppingAsync();
        // BackgroundService.Dispose only signals cancellation; the shared owner releases our semaphore.
        base.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        try { await BeginStoppingAsync().ConfigureAwait(false); }
        finally { DisposeResources(); GC.SuppressFinalize(this); }
    }

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0) return;
        _compilationGate.Dispose();
        _operationsDrained.Dispose();
        base.Dispose();
    }
}
