using System.Buffers.Binary;
using System.Threading.Channels;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;

namespace Mk8.Drava.Application.INF.Proxy.Http2;

internal sealed partial class Http2UpstreamConnection
{
    private readonly Channel<Http2Frame> _received = Channel.CreateBounded<Http2Frame>(new BoundedChannelOptions(4)
    {
        SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait,
    });
    private readonly Http2UpstreamFlowControl _sendFlow = new();
    private readonly Lock _receiveGate = new();
    private readonly CancellationTokenSource _receiveCancellation = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private Task? _receiver;
    private int _receivedWindow = 65535;
    private int _creditDue;
    private Task? _disposeTask;
    private readonly TaskCompletionSource _requestCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _headFramesObserved;
    private bool _continuationExpected;
    private bool _headersEndStream;

    private void StartReceiving(RuntimeTimeouts timeouts)
    {
        lock (_receiveGate) _receiver ??= ReceiveAsync(timeouts, _receiveCancellation.Token);
    }

    private async Task ReceiveAsync(RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (true)
            {
                // The consuming response reader applies head/body idle deadlines. Upload
                // progress has its own deadlines and can legitimately precede any response.
                var frame = await ReadWireFrameAsync(Timeout.InfiniteTimeSpan, ProxyTimeoutKind.UpstreamResponseHead, cancellationToken).ConfigureAwait(false)
                    ?? throw new Http2UpstreamProtocolException("Upstream closed without HTTP/2 response completion.");
                var responseEnded = ValidateContinuation(frame);
                if (await HandleConnectionFrameAsync(frame, timeouts, cancellationToken).ConfigureAwait(false)) continue;
                if (frame.StreamId != _streamId) throw new Http2UpstreamProtocolException("Unexpected upstream HTTP/2 stream.");
                if (frame.Type == Http2FrameType.Data) ChargeReceiveWindow(frame.Payload.Length);
                await _received.Writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                if (responseEnded) return;
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ProxyTimeoutException)
        {
            failure = exception;
            _sendFlow.Fail(exception);
        }
        finally { _received.Writer.TryComplete(failure); }
    }

    private bool ValidateContinuation(Http2Frame frame)
    {
        if (_continuationExpected && (frame.Type != Http2FrameType.Continuation || frame.StreamId != _streamId))
            throw new Http2UpstreamProtocolException("Interleaved HTTP/2 field block.");
        if (!_continuationExpected && frame.Type == Http2FrameType.Continuation)
            throw new Http2UpstreamProtocolException("Unsolicited HTTP/2 CONTINUATION.");
        if (frame.Type == Http2FrameType.Headers)
        {
            _headersEndStream = (frame.Flags & Http2Flags.EndStream) != 0;
            _continuationExpected = (frame.Flags & Http2Flags.EndHeaders) == 0;
        }
        if (frame.Type == Http2FrameType.Continuation) _continuationExpected = (frame.Flags & Http2Flags.EndHeaders) == 0;
        return frame.Type == Http2FrameType.Data && (frame.Flags & Http2Flags.EndStream) != 0
            || frame.Type is Http2FrameType.Headers or Http2FrameType.Continuation && _headersEndStream && !_continuationExpected;
    }

    private void ChargeReceiveWindow(int bytes)
    {
        lock (_receiveGate)
        {
            if (bytes > _receivedWindow) throw new Http2UpstreamProtocolException("Upstream exceeded the advertised receive window.");
            _receivedWindow -= bytes;
        }
    }

    private async ValueTask<Http2Frame?> ReadFrameAsync(TimeSpan timeout, ProxyTimeoutKind kind, CancellationToken cancellationToken)
    {
        if (_receiver is null) return await ReadWireFrameAsync(timeout, kind, cancellationToken).ConfigureAwait(false);
        try
        {
            if (kind == ProxyTimeoutKind.UpstreamResponseHead && !_requestCompleted.Task.IsCompleted && Volatile.Read(ref _headFramesObserved) == 0)
                return await ReadDuringUploadAsync(timeout, cancellationToken).ConfigureAwait(false);
            return await ProxyTimeoutPolicy.RunAsync(async token =>
                await _received.Reader.ReadAsync(token).ConfigureAwait(false), timeout, kind, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
        catch (ChannelClosedException) { return null; }
    }

    private async ValueTask<Http2Frame> ReadDuringUploadAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var read = _received.Reader.ReadAsync(deadline.Token).AsTask();
        try
        {
            if (await Task.WhenAny(read, _requestCompleted.Task).WaitAsync(deadline.Token).ConfigureAwait(false) != read)
                deadline.CancelAfter(timeout);
            return await read.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProxyTimeoutException(ProxyTimeoutKind.UpstreamResponseHead, timeout);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            try { await read.ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or ChannelClosedException) { }
        }
    }

    private async ValueTask ReturnReceiveCreditAsync(RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var bytes = _creditDue;
        _creditDue = 0;
        if (bytes == 0) return;
        lock (_receiveGate) _receivedWindow = checked(_receivedWindow + bytes);
        await SendWindowUpdateAsync(0, bytes, timeouts, cancellationToken).ConfigureAwait(false);
        await SendWindowUpdateAsync(_streamId, bytes, timeouts, cancellationToken).ConfigureAwait(false);
    }

    private void ApplyPeerSettings(Http2Frame frame)
    {
        if (frame.StreamId != 0 || frame.Payload.Length % 6 != 0 || ((frame.Flags & Http2Flags.Ack) != 0 && frame.Payload.Length != 0))
            throw new Http2UpstreamProtocolException("Malformed upstream HTTP/2 SETTINGS.");
        for (var offset = 0; offset < frame.Payload.Length; offset += 6)
        {
            var setting = BinaryPrimitives.ReadUInt16BigEndian(frame.Payload.Span.Slice(offset, 2));
            var value = BinaryPrimitives.ReadUInt32BigEndian(frame.Payload.Span.Slice(offset + 2, 4));
            if (setting == 2) throw new Http2UpstreamProtocolException("A server cannot send SETTINGS_ENABLE_PUSH.");
            if (setting == 4) _sendFlow.SetInitialWindow(value);
            if (setting == 5) _sendFlow.SetMaximumFrameBytes(value);
        }
    }

    private void ApplyWindowUpdate(Http2Frame frame)
    {
        if (frame.Payload.Length != 4 || frame.StreamId != 0 && frame.StreamId != _streamId)
            throw new Http2UpstreamProtocolException("Malformed upstream HTTP/2 WINDOW_UPDATE.");
        var increment = BinaryPrimitives.ReadUInt32BigEndian(frame.Payload.Span) & 0x7fffffffu;
        _sendFlow.AddCredit(frame.StreamId == 0, increment);
    }

    public ValueTask DisposeAsync()
    {
        lock (_receiveGate)
        {
            _disposeTask ??= DisposeOwnedAsync();
            return new ValueTask(_disposeTask);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The private receiver task is started exactly once by this wrapper and is always cancelled and joined before its gates and optional owned stream are disposed. Production callers join their upload/response tasks before disposal; all awaits avoid context capture.")]
    private async Task DisposeOwnedAsync()
    {
        _sendFlow.Fail(new OperationCanceledException("The HTTP/2 exchange was disposed."));
        try
        {
            try
            {
                await _receiveCancellation.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                if (_receiver is not null) await _receiver.ConfigureAwait(false);
            }
        }
        finally
        {
            _receiveCancellation.Dispose();
            _writeGate.Dispose();
            if (!_leaveOpen) await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
