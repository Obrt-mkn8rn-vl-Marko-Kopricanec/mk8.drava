using System.Security.Cryptography;
using Google.Protobuf;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Relay;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RelayShutdownTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownJoinsHalfCloseTransportWritesAndClosesAdmissionAsync(bool synchronousDispose)
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new ControlledRelayReader();
        reader.Send(new RelayFrame { Complete = new Completion { Sha256 = ByteString.CopyFrom(SHA256.HashData([])) } });
        using var writer = new RelayFrameWriter(async (frame, token) =>
        {
            if (frame.FrameCase != RelayFrame.FrameOneofCase.HalfClose) return;
            await using var registration = token.Register(() => canceled.TrySetResult()).ConfigureAwait(false);
            entered.TrySetResult();
            await release.Task.WaitAsync(guard.Token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }, maximumFrames: 1);
        var stream = Create(reader, writer, static () => { });
        await using var lifetime = stream.ConfigureAwait(true);
        var finish = stream.FinishAsync(CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(guard.Token).ConfigureAwait(true);
            if (synchronousDispose) DisposeSynchronously(stream);
            var first = stream.DisposeAsync().AsTask();
            var second = stream.DisposeAsync().AsTask();
            await canceled.Task.WaitAsync(guard.Token).ConfigureAwait(true);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.False(finish.IsCompleted);
            await AssertAdmissionsClosedAsync(stream).ConfigureAwait(true);
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => finish.WaitAsync(guard.Token)).ConfigureAwait(true);
            await Task.WhenAll(first, second).WaitAsync(guard.Token).ConfigureAwait(true);
        }
        finally
        {
            release.TrySetResult();
            try { await finish.WaitAsync(guard.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) when (!guard.IsCancellationRequested) { }
        }
    }

    [Fact]
    public async Task UnexpectedReceiveFailureReleasesReadersAndFinishingCallsAndIsSharedByDisposalAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var failure = new InvalidOperationException("Controlled receive failure.");
        var reader = new ControlledRelayReader();
        using var writer = new RelayFrameWriter(static (_, _) => Task.CompletedTask, maximumFrames: 1);
        var joined = false;
        var finalFailure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var stream = Create(reader, writer, static () => { });
            await using var lifetime = stream.ConfigureAwait(true);
            var read = stream.ReadAsync(new byte[1], CancellationToken.None).AsTask();
            var finish = stream.FinishAsync(CancellationToken.None);
            try
            {
                Assert.False(read.IsCompleted);
                Assert.False(finish.IsCompleted);
                reader.Fail(failure);
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => stream.WaitForCompletionAsync(guard.Token)).ConfigureAwait(true));
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => read.WaitAsync(guard.Token)).ConfigureAwait(true));
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => finish.WaitAsync(guard.Token)).ConfigureAwait(true));
                var first = stream.DisposeAsync().AsTask();
                var second = stream.DisposeAsync().AsTask();
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitAsync(guard.Token)).ConfigureAwait(true));
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => second.WaitAsync(guard.Token)).ConfigureAwait(true));
                await AssertAdmissionsClosedAsync(stream).ConfigureAwait(true);
                joined = true;
            }
            finally { reader.Fail(failure); }
        }).ConfigureAwait(true);
        Assert.True(joined);
        Assert.Same(failure, finalFailure);
    }

    [Fact]
    public async Task ThrowingTransportCancellationStillJoinsThePumpAndIsSharedByDisposalAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var failure = new IOException("Controlled transport cancellation failure.");
        var reader = new ControlledRelayReader();
        using var writer = new RelayFrameWriter(static (_, _) => Task.CompletedTask, maximumFrames: 1);
        var calls = 0;
        var joined = false;
        var finalFailure = await Assert.ThrowsAsync<IOException>(async () =>
        {
            var stream = Create(reader, writer, () => { Interlocked.Increment(ref calls); throw failure; });
            await using var lifetime = stream.ConfigureAwait(true);
            var first = stream.DisposeAsync().AsTask();
            var second = stream.DisposeAsync().AsTask();
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => first.WaitAsync(guard.Token)).ConfigureAwait(true));
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => second.WaitAsync(guard.Token)).ConfigureAwait(true));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.WaitForCompletionAsync(guard.Token)).ConfigureAwait(true);
            await AssertAdmissionsClosedAsync(stream).ConfigureAwait(true);
            joined = true;
        }).ConfigureAwait(true);
        Assert.True(joined);
        Assert.Equal(1, calls);
        Assert.Same(failure, finalFailure);
    }

    private static RelayDuplexStream Create(ControlledRelayReader reader, RelayFrameWriter writer, Action cancelTransport) =>
        RelayDuplexStream.Create(reader, writer, Consumed.Types.Direction.Request, maximumBytes: 1024, cancelTransport, CancellationToken.None);

    [Fact]
    public async Task CancellationCallbackFailureStillJoinsAnOutstandingWriteAndRetainsTransportFailureAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var callbackFailure = new InvalidOperationException("Controlled cancellation callback failure.");
        var transportFailure = new IOException("Controlled transport cancellation failure.");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var writer = new RelayFrameWriter(async (_, token) =>
        {
            await using var registration = token.Register(() => { canceled.TrySetResult(); throw callbackFailure; }).ConfigureAwait(false);
            entered.TrySetResult();
            await release.Task.WaitAsync(guard.Token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }, maximumFrames: 1);
        var joined = false;
        var finalFailure = await Assert.ThrowsAsync<AggregateException>(async () =>
        {
            var stream = Create(new ControlledRelayReader(), writer, () => throw transportFailure);
            await using var lifetime = stream.ConfigureAwait(true);
            var write = stream.WriteAsync(new byte[1], CancellationToken.None).AsTask();
            try
            {
                await entered.Task.WaitAsync(guard.Token).ConfigureAwait(true);
                var disposal = stream.DisposeAsync().AsTask();
                await canceled.Task.WaitAsync(guard.Token).ConfigureAwait(true);
                Assert.False(disposal.IsCompleted);
                Assert.False(write.IsCompleted);
                release.TrySetResult();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write.WaitAsync(guard.Token)).ConfigureAwait(true);
                var failure = await Assert.ThrowsAsync<AggregateException>(() => disposal.WaitAsync(guard.Token)).ConfigureAwait(true);
                Assert.Contains(callbackFailure, failure.Flatten().InnerExceptions);
                Assert.Contains(transportFailure, failure.Flatten().InnerExceptions);
                joined = true;
            }
            finally { release.TrySetResult(); }
        }).ConfigureAwait(true);
        Assert.True(joined);
        Assert.Equal(2, finalFailure.Flatten().InnerExceptions.Count);
    }

    private static async Task AssertAdmissionsClosedAsync(RelayDuplexStream stream)
    {
        Assert.False(stream.CanRead);
        Assert.False(stream.CanWrite);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stream.ReadAsync(new byte[1], CancellationToken.None).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stream.WriteAsync(new byte[1], CancellationToken.None).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stream.CompleteWritesAsync(CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stream.FinishAsync(CancellationToken.None)).ConfigureAwait(true);
    }

    private static void DisposeSynchronously(RelayDuplexStream stream) => stream.Dispose();
}
