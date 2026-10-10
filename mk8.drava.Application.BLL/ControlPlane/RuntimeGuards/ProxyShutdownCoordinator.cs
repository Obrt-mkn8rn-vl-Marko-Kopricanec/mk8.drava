namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
public sealed class ProxyShutdownCoordinator : IDisposable
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _shutdownCts;
    private bool _disposed;
    private int _isShuttingDown;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _deadlineUtc;
    public ProxyShutdownCoordinator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _shutdownCts = new CancellationTokenSource(Timeout.InfiniteTimeSpan, timeProvider);
    }

    public bool IsShuttingDown => Volatile.Read(ref _isShuttingDown) == 1;
    public DateTimeOffset? StartedAtUtc
    {
        get { lock (_gate) return _startedAtUtc; }
    }

    public DateTimeOffset? DeadlineUtc
    {
        get { lock (_gate) return _deadlineUtc; }
    }

    public CancellationToken Token
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _shutdownCts.Token;
            }
        }
    }

    public CancellationToken BeginShutdown(TimeSpan gracePeriod)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_isShuttingDown != 0)
            {
                return _shutdownCts.Token;
            }

            var milliseconds = (long)gracePeriod.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(milliseconds, -1, nameof(gracePeriod));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(milliseconds, (long)uint.MaxValue - 1, nameof(gracePeriod));
            var startedAtUtc = _timeProvider.GetUtcNow();
            var deadlineUtc = startedAtUtc.Add(gracePeriod);
            _startedAtUtc = startedAtUtc;
            _deadlineUtc = deadlineUtc;
            Volatile.Write(ref _isShuttingDown, 1);
            _shutdownCts.CancelAfter(gracePeriod);
            return _shutdownCts.Token;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _shutdownCts.Dispose();
        }
    }
}
