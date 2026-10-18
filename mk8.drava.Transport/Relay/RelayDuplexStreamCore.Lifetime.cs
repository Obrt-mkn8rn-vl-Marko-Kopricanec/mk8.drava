using Grpc.Core;

namespace Mk8.Drava.Transport.Relay;

internal sealed partial class RelayDuplexStreamCore
{
    private readonly Lock _lifetimeGate = new();
    private readonly SemaphoreSlim _operationsDrained = new(0, 1);
    private int _admittedOperations;
    private int _resourcesDisposed;
    private bool _drainSignaled;
    private Task? _cancellationTask;
    private Task? _disposeTask;

    private void EnterOperation()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            _admittedOperations++;
        }
    }

    private void ExitOperation()
    {
        lock (_lifetimeGate)
        {
            _admittedOperations--;
            SignalDrained();
        }
    }

    private void SignalDrained()
    {
        if (_disposed == 0 || _admittedOperations != 0 || _drainSignaled) return;
        _drainSignaled = true;
        _operationsDrained.Release();
    }

    private Task BeginDisposalAsync()
    {
        lock (_lifetimeGate)
        {
            Volatile.Write(ref _disposed, 1);
            SignalDrained();
            // Publish ownership before transport cancellation can reenter DisposeAsync.
            return _disposeTask ??= Task.Run(DisposeOwnedAsync, CancellationToken.None);
        }
    }

    private Task RequestCancellationAsync()
    {
        lock (_lifetimeGate)
            return _cancellationTask ??= Task.Run(CancelTransportAsync, CancellationToken.None);
    }

    private Task CancelTransportAsync() => JoinAllAsync(_lifetime.CancelAsync(), Task.Run(_cancelTransport, CancellationToken.None));

    private async Task DisposeOwnedAsync()
    {
        try
        {
            await JoinAllAsync(RequestCancellationAsync(), JoinPumpAsync(), _operationsDrained.WaitAsync(CancellationToken.None)).ConfigureAwait(false);
        }
        finally { DisposeResources(); }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The pump is created and owned by this stream. Disposal cancels and joins it before releasing synchronization and digest state; the stream never captures a UI context.")]
    private async Task JoinPumpAsync()
    {
        try { await _pump.ConfigureAwait(false); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or RpcException or OperationCanceledException) { }
    }

    private static async Task JoinAllAsync(params Task[] tasks)
    {
        var completion = Task.WhenAll(tasks);
        try { await completion.ConfigureAwait(false); }
        catch
        {
            if (completion.Exception is { InnerExceptions.Count: > 1 } aggregate) throw new AggregateException(aggregate.InnerExceptions);
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _ = BeginDisposalAsync();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        try { await BeginDisposalAsync().ConfigureAwait(false); }
        finally
        {
            DisposeResources();
            await base.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0) return;
        _receivedDigest.Dispose();
        _sentDigest.Dispose();
        _reading.Dispose();
        _writing.Dispose();
        _operationsDrained.Dispose();
        _lifetime.Dispose();
    }
}
