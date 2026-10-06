using System.Threading.Channels;
using Grpc.Core;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Relay;

namespace Mk8.Drava.UnitTests;

internal sealed class RelayStreamPair : IAsyncDisposable
{
    private readonly Channel<RelayFrame> _requests = Channel.CreateUnbounded<RelayFrame>();
    private readonly Channel<RelayFrame> _responses = Channel.CreateUnbounded<RelayFrame>();
    private readonly RelayFrameWriter _clientWriter;
    private readonly RelayFrameWriter _serverWriter;
    private int _clientFrames;
    public RelayDuplexStream Client { get; }
    public RelayDuplexStream Server { get; }
    public int ClientFrames => Volatile.Read(ref _clientFrames);

    public RelayStreamPair(long maximumBytes = 4 * 1024 * 1024, int window = 4)
    {
        _clientWriter = new RelayFrameWriter(async (frame, token) =>
        {
            if (frame.FrameCase == RelayFrame.FrameOneofCase.Data) Interlocked.Increment(ref _clientFrames);
            await _requests.Writer.WriteAsync(frame, token).ConfigureAwait(false);
        }, window);
        _serverWriter = new RelayFrameWriter((frame, token) => _responses.Writer.WriteAsync(frame, token).AsTask(), window);
        Client = RelayDuplexStream.Create(new Reader(_responses.Reader), _clientWriter, Consumed.Types.Direction.Request, maximumBytes, static () => { }, CancellationToken.None);
        Server = RelayDuplexStream.Create(new Reader(_requests.Reader), _serverWriter, Consumed.Types.Direction.Response, maximumBytes, static () => { }, CancellationToken.None);
    }

    public ValueTask InjectRequestAsync(RelayFrame frame) => _requests.Writer.WriteAsync(frame);
    public void EndRequests() => _requests.Writer.TryComplete();
    public void EndResponses() => _responses.Writer.TryComplete();

    public async ValueTask DisposeAsync()
    {
        Client.Dispose(); Server.Dispose();
        await Client.DisposeAsync().ConfigureAwait(false);
        await Server.DisposeAsync().ConfigureAwait(false);
        _clientWriter.Dispose(); _serverWriter.Dispose();
    }

    private sealed class Reader(ChannelReader<RelayFrame> reader) : IAsyncStreamReader<RelayFrame>
    {
        public RelayFrame Current { get; private set; } = new();
        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) return false;
            Current = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
