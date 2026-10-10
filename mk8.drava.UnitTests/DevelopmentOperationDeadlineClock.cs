namespace Mk8.Drava.UnitTests;

// This fixture advances only an operation's one-shot deadline. Network, cleanup and test guards retain real time.
internal sealed class DevelopmentOperationDeadlineClock : TimeProvider
{
    private readonly Lock _gate = new();
    private DeadlineTimer? _timer;
    private TimeSpan _elapsed;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_gate)
        {
            if (_timer is { IsDisposed: false }) throw new InvalidOperationException("The fixture supports one active operation deadline.");
            var timer = new DeadlineTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timer = timer;
            return timer;
        }
    }

    public void Advance(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        TimerCallback? callback;
        object? state;
        lock (_gate)
        {
            _elapsed += duration;
            (callback, state) = _timer?.TakeDueCallback(_elapsed) ?? (null, null);
        }
        callback?.Invoke(state);
    }

    private sealed class DeadlineTimer(DevelopmentOperationDeadlineClock owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan? _due;
        private object? _state = state;
        public bool IsDisposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan) throw new InvalidOperationException("Operation deadlines must be one-shot.");
            if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan) throw new ArgumentOutOfRangeException(nameof(dueTime));
            lock (owner._gate)
            {
                if (IsDisposed) return false;
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._elapsed + dueTime;
                return true;
            }
        }

        public (TimerCallback? Callback, object? State) TakeDueCallback(TimeSpan elapsed)
        {
            if (IsDisposed || _due is null || _due > elapsed) return (null, null);
            _due = null;
            return (callback, _state);
        }

        public void Dispose() => Close();

        private void Close()
        {
            lock (owner._gate)
            {
                IsDisposed = true;
                _due = null;
                _state = null;
            }
        }

        public ValueTask DisposeAsync()
        {
            Close();
            return ValueTask.CompletedTask;
        }
    }
}
