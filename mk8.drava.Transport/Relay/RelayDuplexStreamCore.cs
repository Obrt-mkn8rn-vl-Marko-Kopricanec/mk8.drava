using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Google.Protobuf;
using Grpc.Core;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Transport.Relay;

// One transport reader demultiplexes credits and bytes; credits return only after the caller consumes a whole frame.
internal sealed class RelayDuplexStreamCore : RelayDuplexStream
{
    private readonly IAsyncStreamReader<RelayFrame> _reader;
    private readonly RelayFrameWriter _writer;
    private readonly Consumed.Types.Direction _sendDirection;
    private readonly long _maximumBytes;
    private readonly Action _cancelTransport;
    private readonly CancellationTokenSource _lifetime;
    private readonly Channel<ByteString> _body;
    private readonly SemaphoreSlim _reading = new(1, 1);
    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly BodyDigest _receivedDigest = new();
    private readonly BodyDigest _sentDigest = new();
    private readonly TaskCompletionSource _remoteEnd = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _resourcesEnd = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _pump;
    private ByteString? _current;
    private int _offset;
    private int _inFlight;
    private int _sentEnd;
    private int _disposed;
    private int _resourcesDisposed;
    private int _sentClose;
    private Exception? _failure;

    public RelayDuplexStreamCore(IAsyncStreamReader<RelayFrame> reader, RelayFrameWriter writer, Consumed.Types.Direction sendDirection,
        long maximumBytes, Action cancelTransport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(cancelTransport);
        if (sendDirection is not (Consumed.Types.Direction.Request or Consumed.Types.Direction.Response) || maximumBytes is < 1 or > 4L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Invalid relay stream bounds.");
        _reader = reader; _writer = writer; _sendDirection = sendDirection; _maximumBytes = maximumBytes; _cancelTransport = cancelTransport;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _body = Channel.CreateBounded<ByteString>(new BoundedChannelOptions(writer.MaximumFrames) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        _pump = PumpAsync(_lifetime.Token);
    }

    public override Task WaitForCompletionAsync(CancellationToken cancellationToken) => _pump.WaitAsync(cancellationToken);
    public override bool CanRead => Volatile.Read(ref _disposed) == 0;
    public override bool CanWrite => CanRead && Volatile.Read(ref _sentEnd) == 0;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Relay reads must be asynchronous.");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Relay writes must be asynchronous.");
    public override void Flush() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    public override Task FlushAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Flush(); return Task.CompletedTask; }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (buffer.IsEmpty) return 0;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _reading.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (_current is null)
            {
                if (!await _body.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false)) { ThrowFailure(); return 0; }
                _current = await _body.Reader.ReadAsync(linked.Token).ConfigureAwait(false);
                _offset = 0;
            }
            var count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.Memory.Slice(_offset, count).CopyTo(buffer);
            _offset += count;
            if (_offset == _current.Length)
            {
                _current = null;
                if (Interlocked.Decrement(ref _inFlight) < 0) throw new InvalidDataException("Relay receive credit underflow.");
                await _writer.WriteAsync(new RelayFrame { Consumed = new Consumed { Direction = Opposite(_sendDirection), Frames = 1 } }, linked.Token).ConfigureAwait(false);
            }
            return count;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _failure is not null) { ThrowFailure(); throw; }
        finally { _reading.Release(); }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _writing.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _sentEnd) != 0) throw new InvalidOperationException("Relay upload has already completed.");
            if ((ulong)buffer.Length > (ulong)_maximumBytes - _sentDigest.Bytes) throw new InvalidDataException("Relay upload exceeds its capability byte bound.");
            while (!buffer.IsEmpty)
            {
                var count = Math.Min(buffer.Length, FrameLimits.MaximumFrameBytes);
                var data = buffer[..count];
                _sentDigest.Append(data.Span);
                await _writer.WriteAsync(new RelayFrame { Data = new DataFrame { Payload = ByteString.CopyFrom(data.Span) } }, linked.Token).ConfigureAwait(false);
                buffer = buffer[count..];
            }
        }
        finally { _writing.Release(); }
    }

    public override async Task CompleteWritesAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _writing.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (Interlocked.Exchange(ref _sentEnd, 1) != 0) return;
            await _writer.WriteAsync(new RelayFrame { Complete = _sentDigest.Complete() }, linked.Token).ConfigureAwait(false);
        }
        finally { _writing.Release(); }
    }

    public override async Task FinishAsync(CancellationToken cancellationToken)
    {
        await CompleteWritesAsync(cancellationToken).ConfigureAwait(false);
        await _remoteEnd.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowFailure();
        if (Volatile.Read(ref _inFlight) != 0) throw new InvalidOperationException("Relay half-close requires consumed inbound bytes.");
        if (Interlocked.Exchange(ref _sentClose, 1) != 0) return;
        await _writer.WriteAsync(new RelayFrame { HalfClose = new HalfClose() }, cancellationToken).ConfigureAwait(false);
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        var ended = false;
        var closed = false;
        try
        {
            while (await _reader.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                var frame = _reader.Current;
                if (closed) throw new InvalidDataException("Frames follow relay half-close.");
                if (frame.FrameCase == RelayFrame.FrameOneofCase.Consumed)
                {
                    var consumed = frame.Consumed ?? throw new InvalidDataException("Relay credit is missing.");
                    if (consumed.Direction != _sendDirection) throw new InvalidDataException("Relay credit has the wrong direction.");
                    _writer.Window.Return(consumed.Frames);
                }
                else if (frame.FrameCase == RelayFrame.FrameOneofCase.HalfClose)
                {
                    if (!ended || Volatile.Read(ref _sentEnd) == 0) throw new InvalidDataException("Premature relay half-close.");
                    closed = true;
                }
                else
                {
                    if (ended) throw new InvalidDataException("Bytes follow relay completion.");
                    ended = Receive(frame);
                }
            }
            if (!ended || !closed) throw new EndOfStreamException("Relay transport ended without verified completion and half-close.");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or RpcException or OperationCanceledException)
        {
            Volatile.Write(ref _failure, exception is RpcException ? new IOException("Authenticated relay transport failed.", exception) : exception);
            _body.Writer.TryComplete(_failure);
            _remoteEnd.TrySetResult();
            await _lifetime.CancelAsync().ConfigureAwait(false);
            _cancelTransport();
            throw;
        }
    }

    private bool Receive(RelayFrame frame)
    {
        if (frame.FrameCase == RelayFrame.FrameOneofCase.Reset) throw new IOException("Authenticated node reset the relay.");
        if (frame.FrameCase == RelayFrame.FrameOneofCase.Complete)
        {
            _receivedDigest.Verify(frame.Complete ?? throw new InvalidDataException("Relay completion is missing."));
            _body.Writer.TryComplete();
            _remoteEnd.TrySetResult();
            return true;
        }
        if (frame.FrameCase != RelayFrame.FrameOneofCase.Data) throw new InvalidDataException("Unexpected relay frame.");
        var data = frame.Data ?? throw new InvalidDataException("Relay data is missing.");
        FrameLimits.ValidateData(data);
        if ((ulong)data.Payload.Length > (ulong)_maximumBytes - _receivedDigest.Bytes || Interlocked.Increment(ref _inFlight) > _writer.MaximumFrames)
            throw new InvalidDataException("Relay peer exceeded its byte or credit bound.");
        _receivedDigest.Append(data.Payload.Span);
        if (!_body.Writer.TryWrite(data.Payload)) throw new InvalidDataException("Relay body exceeded its bounded queue.");
        return false;
    }

    private void ThrowFailure()
    {
        if (Volatile.Read(ref _failure) is { } failure) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static Consumed.Types.Direction Opposite(Consumed.Types.Direction direction) =>
        direction == Consumed.Types.Direction.Request ? Consumed.Types.Direction.Response : Consumed.Types.Direction.Request;

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0) { _lifetime.Cancel(); _cancelTransport(); }
        base.Dispose(disposing);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The pump and asynchronous resource-completion source are created and owned by this stream. Disposal cancels and joins them before disposing stream synchronization and hash state; the stream never captures a UI context.")]
    public override async ValueTask DisposeAsync()
    {
        Dispose();
        try { await _pump.ConfigureAwait(false); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or RpcException or OperationCanceledException) { }
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) == 0)
        {
            try
            {
                await _reading.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                await _writing.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                _receivedDigest.Dispose(); _sentDigest.Dispose(); _reading.Dispose(); _writing.Dispose(); _lifetime.Dispose();
            }
            finally { _resourcesEnd.TrySetResult(); }
        }
        else await _resourcesEnd.Task.ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
