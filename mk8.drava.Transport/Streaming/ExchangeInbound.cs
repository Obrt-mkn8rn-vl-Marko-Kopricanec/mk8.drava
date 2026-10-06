using System.Threading.Channels;
using Grpc.Core;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Transport.Streaming;

// One reader separates response credit from request body frames. The body queue cannot exceed its negotiated credit window.
public sealed class ExchangeInbound : IAsyncStreamReader<ExchangeFrame>
{
    private readonly IAsyncStreamReader<ExchangeFrame> _reader;
    private readonly ExchangeServerWriter _writer;
    private readonly Channel<ExchangeFrame> _body;
    private readonly int _maximum;
    private int _requestFrames;
    private int _responseCompleting;

    public void AllowClientCompletion() => Volatile.Write(ref _responseCompleting, 1);

    public ExchangeInbound(IAsyncStreamReader<ExchangeFrame> reader, ExchangeServerWriter writer, int window)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        _reader = reader;
        _writer = writer;
        _maximum = window;
        _body = Channel.CreateBounded<ExchangeFrame>(new BoundedChannelOptions(window + 2) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    }

    public ExchangeFrame Current { get; private set; } = new();

    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        if (!await _body.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) return false;
        Current = await _body.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask RequestConsumedAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Decrement(ref _requestFrames) < 0) throw new InvalidDataException("Invalid request credit accounting.");
        await _writer.WriteAsync(new ExchangeFrame { Consumed = new Consumed { Direction = Consumed.Types.Direction.Request, Frames = 1 } }, cancellationToken).ConfigureAwait(false);
    }

    public async Task PumpAsync(CancellationToken cancellationToken)
    {
        var requestEnded = false;
        try
        {
            while (await _reader.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                var frame = _reader.Current;
                if (frame.FrameCase == ExchangeFrame.FrameOneofCase.Consumed)
                {
                    if (frame.Consumed.Direction != Consumed.Types.Direction.Response) throw new InvalidDataException("Invalid consumption direction.");
                    _writer.ResponseWindow.Return(frame.Consumed.Frames);
                    continue;
                }
                if (requestEnded) throw new InvalidDataException("Frames follow upload completion.");
                if (frame.FrameCase == ExchangeFrame.FrameOneofCase.Data)
                {
                    FrameLimits.ValidateData(frame.Data);
                    if (Interlocked.Increment(ref _requestFrames) > _maximum) throw new InvalidDataException("Request exceeded its credit window.");
                }
                else if (frame.FrameCase is ExchangeFrame.FrameOneofCase.Complete or ExchangeFrame.FrameOneofCase.UploadStopped or ExchangeFrame.FrameOneofCase.Reset)
                    requestEnded = true;
                else if (frame.FrameCase != ExchangeFrame.FrameOneofCase.Trailers)
                    throw new InvalidDataException("Unexpected inbound exchange frame.");
                if (!_body.Writer.TryWrite(frame)) throw new InvalidDataException("Inbound metadata exceeded its bound.");
            }
            if (!requestEnded || Volatile.Read(ref _responseCompleting) == 0)
                throw new EndOfStreamException("Private request stream ended before exchange completion.");
            _body.Writer.TryComplete();
        }
        catch (Exception exception) when (exception is IOException or RpcException or OperationCanceledException)
        {
            _body.Writer.TryComplete(exception);
            throw;
        }
    }
}
