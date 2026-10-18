using Grpc.Core;
using Grpc.Net.Client;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Relay;

namespace Mk8.Drava.Application.INF.NodeRelay;

public sealed partial class RegisteredRelayConnector
{
    private int _resourcesDisposed;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The publication signal and relay pump belong to this connector's admitted connection. Shutdown and disposal join this tracked task before releasing transport resources; all continuations avoid UI context.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Every unexpected connection failure is retained under the connector gate, closes admission, and is rethrown by the shared disposal owner after all connections retire. Catching here preserves later resource cleanup and quiescence.")]
    private async Task FinishOwnerAsync(Task published, string id, RelayDuplexStream stream, RelayFrameWriter writer, AsyncDuplexStreamingCall<RelayFrame, RelayFrame> call, GrpcChannel channel, CancellationTokenSource lifetime)
    {
        await published.ConfigureAwait(false);
        var failures = new List<Exception>();
        try { await stream.WaitForCompletionAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or RpcException or OperationCanceledException) { }
        catch (Exception exception) { failures.Add(exception); }
        try { await RelayConnectionCleanup.DisposeAsync(stream, writer, call, channel, lifetime).ConfigureAwait(false); }
        catch (Exception exception)
        {
            if (failures.Count == 0 || !ReferenceEquals(failures[0], exception)) failures.Add(exception);
        }
        finally
        {
            try { _admission.Release(); }
            catch (Exception exception) { failures.Add(exception); }
            lock (_gate)
            {
                if (failures.Count != 0)
                {
                    // Stop admission on an unexpected cleanup fault, bounding retained failures
                    // to the connections already admitted when this connector starts stopping.
                    _cleanupFailures.AddRange(failures);
                    _ = BeginDisposalAsync();
                }
                _owned.Remove(id);
                SignalQuiescence();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task disposal;
        lock (_gate) disposal = BeginDisposalAsync();
        try { await disposal.ConfigureAwait(false); }
        finally { DisposeResources(); }
    }

    private Task BeginDisposalAsync()
    {
        _disposed = true;
        SignalQuiescence();
        // The caller holds _gate until the shared task is published; callbacks may reenter disposal.
        return _disposeTask ??= Task.Run(DisposeCoreAsync, CancellationToken.None);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The admitted connection tasks are created, tracked and owned by this connector. All concurrent disposal calls join the shared owner and its quiescence signal before admission and controller lifetime state are disposed.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Cancellation callback failures are retained and rethrown after all admitted connections retire. A callback failure must not skip the quiescence wait or resource disposal.")]
    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        try { await _shutdown.CancelAsync().ConfigureAwait(false); }
        catch (Exception exception) { failures.Add(exception); }
        try
        {
            await _quiesced.Task.ConfigureAwait(false);
            lock (_gate) failures.AddRange(_cleanupFailures);
            RelayConnectionCleanup.ThrowFailures(failures);
        }
        finally { DisposeResources(); }
    }

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0) return;
        _shutdown.Dispose();
        _admission.Dispose();
    }

    private void SignalQuiescence()
    {
        if (_disposed && _opening == 0 && _owned.Count == 0) _quiesced.TrySetResult();
    }
}
