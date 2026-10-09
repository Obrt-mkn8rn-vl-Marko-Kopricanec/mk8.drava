using System.Collections.Concurrent;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Connections;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Proxy;

internal sealed partial class GatewayResponseOutput : IGatewayInformationalResponseFeature, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Pipe _pipe = new(new PipeOptions(pauseWriterThreshold: 65536, resumeWriterThreshold: 32768));
    private readonly PipeWriter _destination;
    private readonly ConcurrentQueue<GatewayResponseOutputOperation> _pending = new();
    private readonly SemaphoreSlim _slots = new(8, 8);
    private readonly CancellationTokenSource _admissionStopped = new();
    private readonly CancellationTokenSource _pumpStopped;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly GatewayHttp2OutputBoundary _boundary = new();
    private readonly GatewayCountingPipeWriter _writer;
    private readonly ConnectionContext _connection;
    private readonly Task _pump;
    private readonly bool _http2;
    private long _copied;
    private bool _closed;
    private int _activeWriters;
    private Task? _disposeTask;
    public PipeWriter Writer => _writer;

    public GatewayResponseOutput(ConnectionContext connection, bool http2)
    {
        _connection = connection;
        _destination = connection.Transport.Output;
        _http2 = http2;
        _writer = new GatewayCountingPipeWriter(_pipe.Writer);
        _pumpStopped = CancellationTokenSource.CreateLinkedTokenSource(connection.ConnectionClosed);
        _pump = PumpAsync();
    }

    public async Task WriteAsync(ResponseHead head, int streamId, CancellationToken cancellationToken)
    {
        BeginWrite();
        try { await WriteAdmittedAsync(head, streamId, cancellationToken).ConfigureAwait(false); }
        finally { EndWrite(); }
    }

    private async Task WriteAdmittedAsync(ResponseHead head, int streamId, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _admissionStopped.Token);
        await _slots.WaitAsync(linked.Token).ConfigureAwait(false);
        var transferred = false;
        try
        {
            var bytes = GatewayInformationalEncoder.Encode(head, _http2, streamId);
            var operation = new GatewayResponseOutputOperation(bytes, _writer.Produced, cancellationToken);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                _pending.Enqueue(operation);
                transferred = true;
            }
            using var registration = cancellationToken.Register(_pipe.Reader.CancelPendingRead);
            _pipe.Reader.CancelPendingRead();
            await operation.Completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        finally { if (!transferred) _slots.Release(); }
    }

    private void BeginWrite()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _activeWriters++;
        }
    }

    private void EndWrite()
    {
        lock (_gate)
        {
            _activeWriters--;
            if (_closed && _activeWriters == 0) _drained.TrySetResult();
        }
    }

    private void CloseAdmission()
    {
        lock (_gate)
        {
            _closed = true;
            if (_activeWriters == 0) _drained.TrySetResult();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        CloseAdmission();
        try
        {
            await _admissionStopped.CancelAsync().ConfigureAwait(false);
            await _writer.CompleteAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                // This instance owns the pump; every shutdown path joins it before disposing its resources.
#pragma warning disable VSTHRD003
                try { await _pump.ConfigureAwait(false); }
#pragma warning restore VSTHRD003
                catch (OperationCanceledException) when (_pumpStopped.IsCancellationRequested) { }
            }
            finally
            {
                // Owned continuations signal that all admitted writers have released their registrations.
#pragma warning disable VSTHRD003
                await _drained.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
                _slots.Dispose();
                _admissionStopped.Dispose();
                _pumpStopped.Dispose();
            }
        }
    }
}
