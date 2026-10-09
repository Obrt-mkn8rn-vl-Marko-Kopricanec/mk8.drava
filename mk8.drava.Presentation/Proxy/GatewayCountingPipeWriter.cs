using System.IO.Pipelines;

namespace Mk8.Drava.Presentation.Proxy;

internal sealed class GatewayCountingPipeWriter(PipeWriter inner) : PipeWriter
{
    private long _produced;
    public long Produced => Interlocked.Read(ref _produced);

    public override void Advance(int bytes)
    {
        inner.Advance(bytes);
        Interlocked.Add(ref _produced, bytes);
    }

    public override Memory<byte> GetMemory(int sizeHint = 0) => inner.GetMemory(sizeHint);
    public override Span<byte> GetSpan(int sizeHint = 0) => inner.GetSpan(sizeHint);
    public override void CancelPendingFlush() => inner.CancelPendingFlush();
    public override void Complete(Exception? exception = null) => inner.Complete(exception);
    public override ValueTask CompleteAsync(Exception? exception = null) => inner.CompleteAsync(exception);
    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) => inner.FlushAsync(cancellationToken);
    public override bool CanGetUnflushedBytes => inner.CanGetUnflushedBytes;
    public override long UnflushedBytes => inner.UnflushedBytes;
}
