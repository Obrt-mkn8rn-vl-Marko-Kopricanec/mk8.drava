namespace Mk8.Drava.Transport.Streaming;

// Credits are returned after downstream consumption, rather than when serialization accepts a frame.
public sealed class FrameWindow : IDisposable
{
    private readonly SemaphoreSlim _available;
    private readonly Lock _gate = new();
    private readonly int _maximum;
    private int _inFlight;

    public FrameWindow(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, 8);
        _maximum = maximum;
        _available = new SemaphoreSlim(maximum, maximum);
    }

    public async ValueTask TakeAsync(CancellationToken cancellationToken)
    {
        await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate) _inFlight++;
    }

    public void Return(uint frames)
    {
        lock (_gate)
        {
            if (frames == 0 || frames > (uint)_maximum || frames > (uint)_inFlight) throw new InvalidDataException("Invalid exchange credit acknowledgment.");
            _inFlight -= (int)frames;
            _available.Release((int)frames);
        }
    }

    public void Dispose() => _available.Dispose();
}
