namespace Mk8.Drava.Application.DAL.Registry;

// Admission is counted before waiting, so shutdown joins both the writer and admitted waiters.
internal sealed class RepositoryOperationGate : IAsyncDisposable
{
    private readonly Lock _synchronization = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly SemaphoreSlim _drained = new(0, 1);
    private int _active;
    private bool _closing;
    private bool _drainSignaled;
    private Task? _disposeTask;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_synchronization)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            _active++;
        }
        try { await _serial.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch { CompleteOperation(); throw; }
    }

    public void Release()
    {
        _serial.Release();
        CompleteOperation();
    }

    private void CompleteOperation()
    {
        lock (_synchronization)
        {
            _active--;
            SignalDrained();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_synchronization)
        {
            _closing = true;
            SignalDrained();
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _drained.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _serial.Dispose();
        _drained.Dispose();
    }

    private void SignalDrained()
    {
        if (!_closing || _active != 0 || _drainSignaled) return;
        _drainSignaled = true;
        _drained.Release();
    }
}
