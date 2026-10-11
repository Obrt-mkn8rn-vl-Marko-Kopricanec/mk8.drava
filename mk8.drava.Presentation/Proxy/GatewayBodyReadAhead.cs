using System.IO.Pipelines;
using Mk8.Drava.Transport.Protocol;

namespace Mk8.Drava.Presentation.Proxy;

// The HTTP body remains Kestrel-owned. This scope owns only its bounded pipe and copy task.
internal sealed class GatewayBodyReadAhead : IAsyncDisposable
{
    private readonly Pipe _pipe;
    private readonly CancellationTokenSource _stop;
    private readonly Task _copy;
    private Exception? _copyFailure;
    public Stream Reader { get; }

    public GatewayBodyReadAhead(Stream source, int frameBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameBytes, FrameLimits.MaximumFrameBytes);
        _pipe = new Pipe(new PipeOptions(pauseWriterThreshold: frameBytes, resumeWriterThreshold: Math.Max(1, frameBytes / 2),
            minimumSegmentSize: frameBytes, useSynchronizationContext: false));
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Reader = _pipe.Reader.AsStream(leaveOpen: true);
        _copy = CopyAsync(source, frameBytes);
    }

    private async Task CopyAsync(Stream source, int frameBytes)
    {
        Exception? failure = null;
        try
        {
            var buffer = new byte[frameBytes];
            while (true)
            {
                var count = await source.ReadAsync(buffer, _stop.Token).ConfigureAwait(false);
                if (count == 0) break;
                var flushed = await _pipe.Writer.WriteAsync(buffer.AsMemory(0, count), _stop.Token).ConfigureAwait(false);
                if (flushed.IsCanceled) throw new OperationCanceledException(_stop.Token);
                if (flushed.IsCompleted) break;
            }
        }
        catch (Exception exception)
        {
            // Fault transport: the consumer observes this error instead of an artificial EOF.
            failure = exception;
            _copyFailure = exception;
            throw;
        }
        finally { await _pipe.Writer.CompleteAsync(failure).ConfigureAwait(false); }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "This scope starts the copy task in its constructor and owns it. Disposal cancels and joins that task before releasing the pipe or token source; production awaits avoid context capture.")]
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try { await _copy.ConfigureAwait(false); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        // These source faults are already carried by the pipe; an early stop need not report them twice.
        catch (IOException exception) when (ReferenceEquals(exception, _copyFailure)) { }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, _copyFailure)) { }
        finally
        {
            try
            {
                await Reader.DisposeAsync().ConfigureAwait(false);
                await _pipe.Reader.CompleteAsync().ConfigureAwait(false);
            }
            finally { _stop.Dispose(); }
        }
    }
}
