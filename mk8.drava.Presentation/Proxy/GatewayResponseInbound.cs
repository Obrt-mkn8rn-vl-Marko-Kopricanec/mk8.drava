using System.Threading.Channels;
using Grpc.Core;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Streaming;

namespace Mk8.Drava.Presentation.Proxy;

// Public output backpressure must not prevent parsing upload credit on the same RPC.
internal sealed class GatewayResponseInbound(IAsyncStreamReader<ExchangeFrame> reader, ExchangeClientWriter writer)
{
    // Eight informational heads, final head, trailers, completion and two upload controls.
    private const int MaximumMetadataFrames = 13;
    private readonly Channel<ExchangeFrame> _frames = Channel.CreateBounded<ExchangeFrame>(
        new BoundedChannelOptions(writer.WindowFrames + MaximumMetadataFrames) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private int _responseFrames;

    internal void ResponseConsumed()
    {
        if (Interlocked.Decrement(ref _responseFrames) < 0) throw new InvalidDataException("Invalid response credit accounting.");
    }

    internal async Task ConsumeAsync(Func<ExchangeFrame, Task> consume, CancellationToken cancellationToken)
    {
        await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            await consume(frame).ConfigureAwait(false);
    }

    internal async Task PumpAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        var complete = false;
        var metadataFrames = 0;
        try
        {
            while (await reader.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                if (complete) throw new InvalidDataException("Frames follow response completion.");
                var frame = reader.Current;
                if (frame.FrameCase == ExchangeFrame.FrameOneofCase.Consumed)
                {
                    var consumed = frame.Consumed ?? throw new InvalidDataException("Consumption frame is missing.");
                    if (consumed.Direction != Consumed.Types.Direction.Request) throw new InvalidDataException("Invalid credit direction.");
                    writer.RequestWindow.Return(consumed.Frames);
                    continue;
                }
                if (frame.FrameCase == ExchangeFrame.FrameOneofCase.Data)
                {
                    FrameLimits.ValidateData(frame.Data ?? throw new InvalidDataException("Response data is missing."));
                    if (Interlocked.Increment(ref _responseFrames) > writer.WindowFrames)
                        throw new InvalidDataException("Response exceeded its credit window.");
                }
                else if (++metadataFrames > MaximumMetadataFrames)
                {
                    throw new InvalidDataException("Response metadata exceeded its bound.");
                }
                complete = frame.FrameCase == ExchangeFrame.FrameOneofCase.Complete;
                if (!_frames.Writer.TryWrite(frame)) throw new InvalidDataException("Response metadata exceeded its bound.");
            }
            if (!complete) throw new InvalidDataException("Application ended without response completion.");
        }
        catch (Exception exception)
        {
            // Carry every reader failure to the owned consumer, including a blocked output.
            failure = exception;
            throw;
        }
        finally { _frames.Writer.TryComplete(failure); }
    }
}
