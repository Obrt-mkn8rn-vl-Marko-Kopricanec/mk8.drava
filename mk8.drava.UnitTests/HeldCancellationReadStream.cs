namespace Mk8.Drava.UnitTests;

internal sealed class HeldCancellationReadStream(Stream inner, bool callbackFailure, CancellationToken lifetime) : Stream
{
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource readCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock disposeGate = new();
    private Task? disposal;
    private int disposeCount;

    public Task Stopping => stopping.Task;
    public Task CanceledReadCompleted => readCompleted.Task;
    public int DisposeCount => Volatile.Read(ref disposeCount);
    public void ReleaseRead() => release.TrySetResult();
    public override bool CanRead => inner.CanRead;
    public override bool CanWrite => inner.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var canceled = false;
        try
        {
            var callback = callbackFailure
                ? cancellationToken.Register(static () => throw new IOException("Owned cancellation callback failure."))
                : default;
            await using var callbackLifetime = callback.ConfigureAwait(false);
            try
            {
                return await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                stopping.TrySetResult();
                await release.Task.WaitAsync(lifetime).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            if (canceled) readCompleted.TrySetResult();
        }
    }
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override async ValueTask DisposeAsync()
    {
        Task completion;
        lock (disposeGate)
        {
            completion = disposal ??= DisposeOwnedAsync();
        }
        try
        {
            await completion.ConfigureAwait(false);
        }
        finally
        {
            await base.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
    private async Task DisposeOwnedAsync()
    {
        Interlocked.Increment(ref disposeCount);
        await inner.DisposeAsync().ConfigureAwait(false);
    }
}
