namespace Mk8.Drava.UnitTests;

internal sealed class HeldTransportRetirementStream(Stream inner, bool failure, CancellationToken lifetime) : Stream
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeCount;

    public Task Started => _started.Task;
    public int DisposeCount => Volatile.Read(ref _disposeCount);
    public void Release() => _release.TrySetResult();
    public override bool CanRead => inner.CanRead;
    public override bool CanWrite => inner.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCount);
        _started.TrySetResult();
        try
        {
            await _release.Task.WaitAsync(lifetime).ConfigureAwait(false);
            await inner.DisposeAsync().ConfigureAwait(false);
            if (failure) throw new IOException("Controlled transport retirement failure.");
        }
        finally
        {
            await base.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}
