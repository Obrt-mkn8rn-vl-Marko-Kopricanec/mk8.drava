using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Streaming;

namespace Mk8.Drava.Transport.Relay;

public sealed class RelayFrameWriter : IDisposable
{
    private readonly SemaphoreSlim _serialization = new(1, 1);
    private readonly Func<RelayFrame, CancellationToken, Task> _write;
    public FrameWindow Window { get; }
    public int MaximumFrames { get; }

    public RelayFrameWriter(Func<RelayFrame, CancellationToken, Task> write, int maximumFrames)
    {
        ArgumentNullException.ThrowIfNull(write);
        _write = write;
        Window = new FrameWindow(maximumFrames);
        MaximumFrames = maximumFrames;
    }

    public async Task WriteAsync(RelayFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.FrameCase == RelayFrame.FrameOneofCase.Data) await Window.TakeAsync(cancellationToken).ConfigureAwait(false);
        await _serialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _write(frame, cancellationToken).ConfigureAwait(false); }
        finally { _serialization.Release(); }
    }

    public void Dispose() { Window.Dispose(); _serialization.Dispose(); }
}
