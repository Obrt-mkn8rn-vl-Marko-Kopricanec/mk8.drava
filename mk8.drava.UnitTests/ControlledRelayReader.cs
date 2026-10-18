using System.Threading.Channels;
using Grpc.Core;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.UnitTests;

internal sealed class ControlledRelayReader : IAsyncStreamReader<RelayFrame>
{
    private readonly Channel<RelayFrame> _frames = Channel.CreateUnbounded<RelayFrame>();

    public RelayFrame Current { get; private set; } = new();

    public void Send(RelayFrame frame)
    {
        if (!_frames.Writer.TryWrite(frame)) throw new InvalidOperationException("Controlled relay reader is closed.");
    }

    public void Fail(Exception failure) => _frames.Writer.TryComplete(failure);

    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        if (!await _frames.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) return false;
        Current = await _frames.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
