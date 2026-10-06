using Grpc.Core;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Transport.Relay;

public abstract class RelayDuplexStream : Stream
{
    public static RelayDuplexStream Create(IAsyncStreamReader<RelayFrame> reader, RelayFrameWriter writer, Consumed.Types.Direction sendDirection,
        long maximumBytes, Action cancelTransport, CancellationToken cancellationToken) =>
        new RelayDuplexStreamCore(reader, writer, sendDirection, maximumBytes, cancelTransport, cancellationToken);

    public abstract Task WaitForCompletionAsync(CancellationToken cancellationToken);
    public abstract Task CompleteWritesAsync(CancellationToken cancellationToken);
    public abstract Task FinishAsync(CancellationToken cancellationToken);
}
