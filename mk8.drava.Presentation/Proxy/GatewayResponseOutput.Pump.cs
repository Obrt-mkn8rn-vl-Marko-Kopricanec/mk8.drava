using System.Buffers;
using Microsoft.AspNetCore.Connections;

namespace Mk8.Drava.Presentation.Proxy;

internal sealed partial class GatewayResponseOutput
{
    private async Task PumpAsync()
    {
        Exception? failure = null;
        try { await CopyOutputAsync().ConfigureAwait(false); }
        catch (Exception exception)
        {
            failure = exception;
            if (!_pumpStopped.IsCancellationRequested)
                _connection.Abort(new ConnectionAbortedException("Gateway response output failed.", exception));
            throw;
        }
        finally
        {
            CloseAdmission();
            await _admissionStopped.CancelAsync().ConfigureAwait(false);
            FailPending(failure);
            await _pipe.Reader.CompleteAsync(failure).ConfigureAwait(false);
        }
    }

    private async Task CopyOutputAsync()
    {
        while (true)
        {
            await WritePendingAsync().ConfigureAwait(false);
            var result = await _pipe.Reader.ReadAsync(_pumpStopped.Token).ConfigureAwait(false);
            var buffer = result.Buffer;
            try
            {
                foreach (var memory in buffer) await CopySegmentAsync(memory).ConfigureAwait(false);
                await WritePendingAsync().ConfigureAwait(false);
                if (!buffer.IsEmpty)
                {
                    var flushed = await _destination.FlushAsync(_pumpStopped.Token).ConfigureAwait(false);
                    if (flushed.IsCanceled) throw new OperationCanceledException(_pumpStopped.Token);
                    if (flushed.IsCompleted) break;
                }
            }
            finally { _pipe.Reader.AdvanceTo(buffer.End); }
            if (result.IsCompleted) break;
        }
    }

    private async Task CopySegmentAsync(ReadOnlyMemory<byte> memory)
    {
        var offset = 0;
        while (offset < memory.Length)
        {
            var count = _http2 ? _boundary.Advance(memory.Span[offset..]) : memory.Length - offset;
            if (!_http2 && _pending.TryPeek(out var first) && first.Fence > _copied)
                count = (int)Math.Min(count, first.Fence - _copied);
            _destination.Write(memory.Span.Slice(offset, count));
            _copied += count;
            offset += count;
            await WritePendingAsync().ConfigureAwait(false);
        }
    }

    private async Task WritePendingAsync()
    {
        while (_pending.TryPeek(out var operation))
        {
            if (!operation.Token.IsCancellationRequested && ((_http2 && !_boundary.CanInsert) || _copied < operation.Fence)) return;
            if (!_pending.TryDequeue(out operation)) continue;
            try { await WriteOperationAsync(operation).ConfigureAwait(false); }
            finally { _slots.Release(); }
        }
    }

    private async Task WriteOperationAsync(GatewayResponseOutputOperation operation)
    {
        try
        {
            if (operation.Token.IsCancellationRequested)
            {
                operation.Completion.TrySetCanceled(operation.Token);
                return;
            }
            var result = await _destination.WriteAsync(operation.Bytes, _pumpStopped.Token).ConfigureAwait(false);
            if (result.IsCanceled) throw new OperationCanceledException(_pumpStopped.Token);
            if (result.IsCompleted) throw new IOException("Client closed informational response output.");
            operation.Completion.TrySetResult();
        }
        catch (Exception exception)
        {
            operation.Completion.TrySetException(exception);
            throw;
        }
    }

    private void FailPending(Exception? failure)
    {
        while (_pending.TryDequeue(out var operation))
        {
            operation.Completion.TrySetException(failure ?? new IOException("Output ended before informational response."));
            _slots.Release();
        }
    }
}
