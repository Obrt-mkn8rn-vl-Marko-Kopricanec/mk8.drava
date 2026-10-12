using Google.Protobuf;
using Grpc.Core;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Streaming;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ExchangeClientObservationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "This test starts the send task and owns its cancellation source; both assertion and finally join that task before disposing the writer.")]
    public async Task CancelledCreditWaitNeverSubmitsAFrameOrFabricatesCreditAsync(bool observe)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sink = new WaitingClientWriter();
        using var writer = new ExchangeClientWriter(sink, 1, stop.Token) { Observation = observe ? new ExchangeWriteObservation() : null };
        await writer.RequestWindow.TakeAsync(stop.Token).ConfigureAwait(true);
        var send = writer.WriteAsync(Payload(), stop.Token);
        try
        {
            Assert.False(send.IsCompleted);
            Assert.Equal(0, sink.Submissions);
            await stop.CancelAsync().ConfigureAwait(true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await send.ConfigureAwait(true)).ConfigureAwait(true);
            Assert.Equal(0, sink.Submissions);
            writer.RequestWindow.Return(1);
            Assert.Throws<InvalidDataException>(() => writer.RequestWindow.Return(1));
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(true);
            try { await send.ConfigureAwait(true); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "This test starts the send task and owns its cancellation source; both assertion and finally join that task before disposing the writer.")]
    public async Task CancelledTransportWriteSettlesWithoutAnotherSubmissionAsync(bool observe)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sink = new WaitingClientWriter();
        using var writer = new ExchangeClientWriter(sink, 1, stop.Token) { Observation = observe ? new ExchangeWriteObservation() : null };
        var send = writer.WriteAsync(Payload(), stop.Token);
        try
        {
            await sink.Entered.Task.WaitAsync(stop.Token).ConfigureAwait(true);
            Assert.False(send.IsCompleted);
            await stop.CancelAsync().ConfigureAwait(true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await send.ConfigureAwait(true)).ConfigureAwait(true);
            Assert.True(sink.Exited.Task.IsCompletedSuccessfully);
            Assert.Equal(1, sink.Submissions);
            writer.RequestWindow.Return(1);
            Assert.Throws<InvalidDataException>(() => writer.RequestWindow.Return(1));
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(true);
            try { await send.ConfigureAwait(true); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    private static ExchangeFrame Payload() => new() { Data = new DataFrame { Payload = ByteString.CopyFromUtf8("fixture") } };

    private sealed class WaitingClientWriter : IClientStreamWriter<ExchangeFrame>
    {
        public WriteOptions? WriteOptions { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Submissions { get; private set; }
        public Task WriteAsync(ExchangeFrame message) => WriteAsync(message, CancellationToken.None);
        public async Task WriteAsync(ExchangeFrame message, CancellationToken cancellationToken)
        {
            Submissions++;
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); }
            finally { Exited.TrySetResult(); }
        }
        public Task CompleteAsync() => Task.CompletedTask;
    }
}
