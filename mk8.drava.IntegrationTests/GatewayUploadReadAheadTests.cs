using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Presentation.Proxy;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class GatewayUploadReadAheadTests
{
    [Fact]
    public async Task AvailableFragmentsAreBatchedWithoutWaitingForTheWholeFrameAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var source = new FragmentSource(pauseAtWindow: true);
        await using var sourceLifetime = source.ConfigureAwait(true);
        using var writer = new GatedWriter(blockFirst: true);
        var context = new DefaultHttpContext();
        context.Request.Body = source;
        using var upload = new GatewayUpload(context, writer, 65536, 8192);
        upload.Allow();
        var sending = upload.SendAsync(hasBody: true);
        try
        {
            await writer.FirstFrame.Task.WaitAsync(deadline.Token).ConfigureAwait(true);
            Assert.Equal(1024, writer.FirstLength);
            Assert.False(source.AllowRest.Task.IsCompleted);
            source.AllowRest.TrySetResult();
            await source.WindowRead.Task.WaitAsync(deadline.Token).ConfigureAwait(true);
            Assert.Equal(8192, source.BytesRead);
            writer.ReleaseFirst.TrySetResult();
            await writer.SecondFrame.Task.WaitAsync(deadline.Token).ConfigureAwait(true);
            Assert.Equal(7168, writer.SecondLength);
            Assert.False(source.AllowTail.Task.IsCompleted);
            source.AllowTail.TrySetResult();
            await sending.WaitAsync(deadline.Token).ConfigureAwait(true);
            Assert.True(upload.Completed);
            Assert.True(writer.Complete);
            Assert.Equal(65536UL, writer.Bytes);
            Assert.Equal(0, source.ActiveReads);
        }
        finally
        {
            source.AllowRest.TrySetResult();
            source.AllowTail.TrySetResult();
            writer.ReleaseFirst.TrySetResult();
            if (!sending.IsCompleted) await upload.StopAsync(sending).ConfigureAwait(true);
            else await sending.ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task EarlyStopJoinsThePendingBorrowedSourceReadBeforeAcknowledgingAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var source = new FragmentSource(pauseAtWindow: false);
        await using var sourceLifetime = source.ConfigureAwait(true);
        using var writer = new GatedWriter(blockFirst: true);
        var context = new DefaultHttpContext();
        context.Request.Body = source;
        using var upload = new GatewayUpload(context, writer, 65536, 8192);
        upload.Allow();
        var sending = upload.SendAsync(hasBody: true);
        try
        {
            await writer.FirstFrame.Task.WaitAsync(deadline.Token).ConfigureAwait(true);
            await source.RestReadPending.Task.WaitAsync(deadline.Token).ConfigureAwait(true);
            await upload.StopAsync(sending).WaitAsync(deadline.Token).ConfigureAwait(true);
            Assert.Equal(0, source.ActiveReads);
            Assert.False(source.Disposed);
            Assert.False(upload.Completed);
            Assert.False(writer.Complete);
            Assert.True(writer.Stopped);
            Assert.Equal(1, writer.DataFrames);
        }
        finally
        {
            if (!sending.IsCompleted) await upload.StopAsync(sending).ConfigureAwait(true);
            else await sending.ConfigureAwait(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceFailureCannotBecomeSuccessfulUploadCompletionAsync(bool ioFailure)
    {
        Exception expected = ioFailure ? new IOException("synthetic source failure") : new InvalidOperationException("synthetic source failure");
        var source = new FragmentSource(pauseAtWindow: false, failure: expected);
        await using var sourceLifetime = source.ConfigureAwait(true);
        using var writer = new GatedWriter(blockFirst: false);
        var context = new DefaultHttpContext();
        context.Request.Body = source;
        using var upload = new GatewayUpload(context, writer, 65536, 8192);
        upload.Allow();
        var exception = await Record.ExceptionAsync(() => upload.SendAsync(hasBody: true)).ConfigureAwait(true);
        Assert.NotNull(exception);
        Assert.Equal(expected.GetType(), exception.GetType());
        Assert.Equal(expected.Message, exception.Message);
        Assert.False(upload.Completed);
        Assert.False(writer.Complete);
        Assert.Equal(0, source.ActiveReads);
        Assert.False(source.Disposed);
    }

    private sealed class FragmentSource(bool pauseAtWindow, Exception? failure = null) : Stream
    {
        private int _position;
        private int _activeReads;
        public int BytesRead => Volatile.Read(ref _position);
        public int ActiveReads => Volatile.Read(ref _activeReads);
        public bool Disposed { get; private set; }
        public TaskCompletionSource AllowRest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowTail { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WindowRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RestReadPending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => !Disposed;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _activeReads);
            try
            {
                if (_position >= 1024)
                {
                    if (failure is not null) throw failure;
                    RestReadPending.TrySetResult();
                    await AllowRest.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                if (pauseAtWindow && _position == 8192)
                {
                    WindowRead.TrySetResult();
                    await AllowTail.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                var count = Math.Min(1024, Math.Min(buffer.Length, 65536 - _position));
                buffer.Span[..count].Fill(7);
                Interlocked.Add(ref _position, count);
                return count;
            }
            finally { Interlocked.Decrement(ref _activeReads); }
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class GatedWriter(bool blockFirst) : IClientStreamWriter<ExchangeFrame>, IDisposable
    {
        private readonly BodyDigest _digest = new();
        public TaskCompletionSource FirstFrame { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondFrame { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WriteOptions? WriteOptions { get; set; }
        public int FirstLength { get; private set; }
        public int SecondLength { get; private set; }
        public int DataFrames { get; private set; }
        public ulong Bytes => _digest.Bytes;
        public bool Complete { get; private set; }
        public bool Stopped { get; private set; }
        public Task WriteAsync(ExchangeFrame message) => WriteAsync(message, CancellationToken.None);

        public async Task WriteAsync(ExchangeFrame message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (message.FrameCase)
            {
                case ExchangeFrame.FrameOneofCase.Data:
                    var data = message.Data ?? throw new InvalidDataException("Missing data.");
                    FrameLimits.ValidateData(data);
                    _digest.Append(data.Payload.Span);
                    if (++DataFrames == 1)
                    {
                        FirstLength = data.Payload.Length;
                        FirstFrame.TrySetResult();
                        if (blockFirst) await ReleaseFirst.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else if (DataFrames == 2) { SecondLength = data.Payload.Length; SecondFrame.TrySetResult(); }
                    break;
                case ExchangeFrame.FrameOneofCase.Complete:
                    _digest.Verify(message.Complete ?? throw new InvalidDataException("Missing completion."));
                    Complete = true;
                    break;
                case ExchangeFrame.FrameOneofCase.UploadStopped:
                    Stopped = true;
                    break;
                default: throw new InvalidDataException("Unexpected frame.");
            }
        }

        public Task CompleteAsync() => Task.CompletedTask;
        public void Dispose() => _digest.Dispose();
    }
}
