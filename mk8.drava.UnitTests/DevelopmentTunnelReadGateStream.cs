namespace Mk8.Drava.UnitTests;

// Cancellation requests cleanup; only the owning test releases the read, so settlement is independently observable.
internal sealed class DevelopmentTunnelReadGateStream(Exception? failure, CancellationToken settlementGuard, Exception? cancellationFailure = null) : Stream
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public void Release() => _release.TrySetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var registration = cancellationToken.Register(() =>
        {
            CancellationObserved.TrySetResult();
            if (cancellationFailure is not null) throw cancellationFailure;
        });
        await using var registrationLifetime = registration.ConfigureAwait(false);
        Entered.TrySetResult();
        try
        {
            await _release.Task.WaitAsync(settlementGuard).ConfigureAwait(false);
            if (failure is not null) throw failure;
            cancellationToken.ThrowIfCancellationRequested();
            return 0;
        }
        finally { Settled.TrySetResult(); }
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
