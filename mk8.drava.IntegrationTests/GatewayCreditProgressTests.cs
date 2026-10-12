using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Presentation.Proxy;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Streaming;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class GatewayCreditProgressTests
{
    [Fact]
    public async Task RequestCreditProgressesWhileThePublicResponseWriteIsBlockedAsync()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var body = new BlockedPublicBody();
        await using var bodyLifetime = body.ConfigureAwait(true);
        var context = new DefaultHttpContext { RequestAborted = stop.Token };
        context.Response.Body = body;
        var sink = new RequestSink();
        using var writer = new ExchangeClientWriter(sink, 1, stop.Token);
        await writer.RequestWindow.TakeAsync(stop.Token).ConfigureAwait(true);
        using var upload = new GatewayUpload(context, writer, 1024, 1024);
        using var digest = new BodyDigest();
        digest.Append("response"u8);
        var reader = new QueuedResponseReader([
            new ExchangeFrame { Response = new ResponseHead { StatusCode = 200 } },
            new ExchangeFrame { Data = new DataFrame { Payload = ByteString.CopyFromUtf8("response") } },
            new ExchangeFrame { Consumed = new Consumed { Direction = Consumed.Types.Direction.Request, Frames = 1 } },
            new ExchangeFrame { Complete = digest.Complete() }]);
        var response = new GatewayResponse(context, reader, writer, upload, static () => Task.CompletedTask).ReceiveAsync();
        Task? next = null;
        try
        {
            await body.Entered.Task.WaitAsync(stop.Token).ConfigureAwait(true);
            next = writer.RequestWindow.TakeAsync(stop.Token).AsTask();
            await next.WaitAsync(stop.Token).ConfigureAwait(true);
            Assert.False(body.Release.Task.IsCompleted);
            Assert.False(response.IsCompleted);
            Assert.Equal(0U, sink.ResponseCredits);
            body.Release.TrySetResult();
            await response.WaitAsync(stop.Token).ConfigureAwait(true);
            Assert.Equal(1U, sink.ResponseCredits);
            Assert.Equal("response"u8.ToArray(), body.ToArray());
        }
        finally
        {
            body.Release.TrySetResult();
            await stop.CancelAsync().ConfigureAwait(true);
            try { await response.ConfigureAwait(true); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            if (next is not null)
            {
                try { await next.ConfigureAwait(true); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task InvalidPrivateFramesCancelAndJoinBlockedPublicOutputAsync(int fault)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var body = new BlockedPublicBody();
        await using var bodyLifetime = body.ConfigureAwait(true);
        var context = new DefaultHttpContext { RequestAborted = stop.Token };
        context.Response.Body = body;
        var sink = new RequestSink();
        using var writer = new ExchangeClientWriter(sink, 1, stop.Token);
        using var upload = new GatewayUpload(context, writer, 1024, 1024);
        List<ExchangeFrame> frames =
        [
            new() { Response = new ResponseHead { StatusCode = 200 } },
            new() { Data = new DataFrame { Payload = ByteString.CopyFromUtf8("response") } },
        ];
        switch (fault)
        {
            case 0:
                frames.Add(new ExchangeFrame { Data = new DataFrame { Payload = ByteString.CopyFromUtf8("excess") } });
                break;
            case 1:
                for (var index = 0; index < 14; index++) frames.Add(new ExchangeFrame { UploadAllowed = new UploadAllowed() });
                break;
            case 2:
                frames.Add(new ExchangeFrame { Consumed = new Consumed { Direction = Consumed.Types.Direction.Response, Frames = 1 } });
                break;
            case 4:
                using (var digest = new BodyDigest())
                {
                    digest.Append("response"u8);
                    frames.Add(new ExchangeFrame { Complete = digest.Complete() });
                }
                frames.Add(new ExchangeFrame { Reset = new Reset() });
                break;
        }
        var reader = new AfterPublicWriteReader(body, [.. frames]);
        var response = new GatewayResponse(context, reader, writer, upload, static () => Task.CompletedTask).ReceiveAsync();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => response.WaitAsync(stop.Token)).ConfigureAwait(true);
            Assert.True(body.Entered.Task.IsCompletedSuccessfully);
            Assert.True(body.Exited.Task.IsCompletedSuccessfully);
            Assert.False(body.Release.Task.IsCompleted);
            Assert.Equal(0U, sink.ResponseCredits);
            Assert.Empty(body.ToArray());
        }
        finally
        {
            body.Release.TrySetResult();
            await stop.CancelAsync().ConfigureAwait(true);
            try { await response.ConfigureAwait(true); }
            catch (Exception exception) when (exception is InvalidDataException or OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task PublicOutputFailureCancelsAndJoinsTheWaitingPrivateReaderAsync()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var body = new FailedPublicBody();
        await using var bodyLifetime = body.ConfigureAwait(true);
        var context = new DefaultHttpContext { RequestAborted = stop.Token };
        context.Response.Body = body;
        var sink = new RequestSink();
        using var writer = new ExchangeClientWriter(sink, 1, stop.Token);
        using var upload = new GatewayUpload(context, writer, 1024, 1024);
        var reader = new WaitingResponseReader();
        var response = new GatewayResponse(context, reader, writer, upload, static () => Task.CompletedTask).ReceiveAsync();
        try
        {
            await reader.Waiting.Task.WaitAsync(stop.Token).ConfigureAwait(true);
            body.Fail.TrySetResult();
            await Assert.ThrowsAsync<IOException>(() => response.WaitAsync(stop.Token)).ConfigureAwait(true);
            Assert.True(reader.Exited.Task.IsCompletedSuccessfully);
            Assert.Equal(0U, sink.ResponseCredits);
            Assert.Empty(body.ToArray());
        }
        finally
        {
            body.Fail.TrySetResult();
            await stop.CancelAsync().ConfigureAwait(true);
            try { await response.ConfigureAwait(true); }
            catch (Exception exception) when (exception is IOException or OperationCanceledException) { }
        }
    }

    private sealed class AfterPublicWriteReader(BlockedPublicBody body, ExchangeFrame[] frames) : IAsyncStreamReader<ExchangeFrame>
    {
        private int _next;
        public ExchangeFrame Current { get; private set; } = new();
        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_next == 2) await body.Entered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (_next == frames.Length) return false;
            Current = frames[_next++];
            return true;
        }
    }

    private sealed class FailedPublicBody : MemoryStream
    {
        internal TaskCompletionSource Fail { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Fail.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            throw new IOException("Controlled public output failure.");
        }
    }

    private sealed class WaitingResponseReader : IAsyncStreamReader<ExchangeFrame>
    {
        private int _next;
        public ExchangeFrame Current { get; private set; } = new();
        internal TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_next++ == 0) { Current = new ExchangeFrame { Response = new ResponseHead { StatusCode = 200 } }; return true; }
            if (_next == 2) { Current = new ExchangeFrame { Data = new DataFrame { Payload = ByteString.CopyFromUtf8("response") } }; return true; }
            Waiting.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); }
            finally { Exited.TrySetResult(); }
            return false;
        }
    }

    private sealed class BlockedPublicBody : MemoryStream
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            finally { Exited.TrySetResult(); }
        }
    }

    private sealed class QueuedResponseReader(ExchangeFrame[] frames) : IAsyncStreamReader<ExchangeFrame>
    {
        private int _next;
        public ExchangeFrame Current { get; private set; } = new();
        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_next == frames.Length) return Task.FromResult(false);
            Current = frames[_next++];
            return Task.FromResult(true);
        }
    }

    private sealed class RequestSink : IClientStreamWriter<ExchangeFrame>
    {
        public WriteOptions? WriteOptions { get; set; }
        internal uint ResponseCredits { get; private set; }
        public Task WriteAsync(ExchangeFrame message) => WriteAsync(message, CancellationToken.None);
        public Task WriteAsync(ExchangeFrame message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(ExchangeFrame.FrameOneofCase.Consumed, message.FrameCase);
            var consumed = message.Consumed ?? throw new InvalidDataException("Expected response consumption credit.");
            Assert.Equal(Consumed.Types.Direction.Response, consumed.Direction);
            ResponseCredits += consumed.Frames;
            return Task.CompletedTask;
        }
        public Task CompleteAsync() => Task.CompletedTask;
    }
}
