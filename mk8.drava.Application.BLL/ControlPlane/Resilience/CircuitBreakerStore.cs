using System.Collections.Concurrent;

namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public sealed partial class CircuitBreakerStore
{
    private void RefreshOpenState(CircuitBreakerPolicyInput policy, MutableCircuitState state, DateTimeOffset now)
    {
        if (state.State != CircuitBreakerRuntimeState.Open || state.OpenedAtUtc is null)
        {
            return;
        }

        if (now - state.OpenedAtUtc.Value >= policy.OpenDuration)
        {
            state.MoveToHalfOpen();
            _metrics.CircuitHalfOpened();
        }
    }

    private void Open(MutableCircuitState state, DateTimeOffset now)
    {
        state.MoveToOpen(now);
        _metrics.CircuitOpened();
    }

    private void Close(MutableCircuitState state)
    {
        state.MoveToClosed();
        _metrics.CircuitClosed();
    }

    private void ReleaseHalfOpenProbe(CircuitBreakerLease lease)
    {
        if (!lease.Enabled || !lease.HalfOpenProbe)
        {
            return;
        }

        var state = GetOrCreate(lease.Source.UpstreamIdentity);
        lock (state.Gate)
        {
            state.RecordHalfOpenProbeCompleted();
        }
    }

    private static bool ContainsStatus(IReadOnlyList<int> statusCodes, int statusCode)
    {
        return statusCodes.Any(code => code == statusCode);
    }

    private static string NormalizeReason(string reason)
    {
        #pragma warning disable CA1308 // Failure reasons are emitted as canonical lower-case underscore status labels; uppercase would change the observable diagnostic contract.
        return string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim().ToLowerInvariant().Replace(' ', '_');
        #pragma warning restore CA1308
    }

    private sealed class MutableCircuitState
    {
        public object Gate { get; } = new();
        public CircuitBreakerRuntimeState State { get; private set; } = CircuitBreakerRuntimeState.Closed;
        public DateTimeOffset? WindowStartedAtUtc { get; private set; }
        public DateTimeOffset? OpenedAtUtc { get; private set; }
        public int FailureCount { get; private set; }
        public int HalfOpenInFlight { get; private set; }
        public long RejectedRequests { get; private set; }
        public string? LastFailureReason { get; private set; }

        public void MoveToHalfOpen()
        {
            State = CircuitBreakerRuntimeState.HalfOpen;
            ResetHalfOpenProbes();
        }

        public void MoveToOpen(DateTimeOffset openedAtUtc)
        {
            State = CircuitBreakerRuntimeState.Open;
            OpenedAtUtc = openedAtUtc;
            ResetHalfOpenProbes();
            ResetFailureWindow(openedAtUtc);
        }

        public void MoveToClosed()
        {
            State = CircuitBreakerRuntimeState.Closed;
            OpenedAtUtc = null;
            ClearFailureTracking();
            ResetHalfOpenProbes();
        }

        public void RecordRejectedRequest()
        {
            RejectedRequests++;
        }

        public int RecordFailure(string reason, DateTimeOffset now, TimeSpan samplingWindow)
        {
            RecordFailureReason(reason);
            if (WindowStartedAtUtc is null || now - WindowStartedAtUtc > samplingWindow)
            {
                WindowStartedAtUtc = now;
                FailureCount = 0;
            }

            FailureCount++;
            return FailureCount;
        }

        public void RecordFailureReason(string reason)
        {
            LastFailureReason = reason;
        }

        public void ResetFailureWindow(DateTimeOffset? windowStartedAtUtc)
        {
            WindowStartedAtUtc = windowStartedAtUtc;
            FailureCount = 0;
        }

        public void ClearFailureTracking()
        {
            ResetFailureWindow(windowStartedAtUtc: null);
            LastFailureReason = null;
        }

        public void RecordHalfOpenProbeStarted()
        {
            HalfOpenInFlight++;
        }

        public void RecordHalfOpenProbeCompleted()
        {
            HalfOpenInFlight = Math.Max(0, HalfOpenInFlight - 1);
        }

        public void ResetHalfOpenProbes()
        {
            HalfOpenInFlight = 0;
        }
    }

    private readonly ConcurrentDictionary<string, MutableCircuitState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly IProxyCircuitBreakerMetricsSink _metrics;
    private readonly TimeProvider _timeProvider;
    public CircuitBreakerStore(IProxyCircuitBreakerMetricsSink metrics, TimeProvider timeProvider)
    {
        _metrics = metrics;
        _timeProvider = timeProvider;
    }

    public bool IsAvailable(CircuitBreakerStatusSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Policy.Enabled)
        {
            return true;
        }

        var state = GetOrCreate(source.UpstreamIdentity);
        lock (state.Gate)
        {
            RefreshOpenState(source.Policy, state, _timeProvider.GetUtcNow());
            return state.State switch
            {
                CircuitBreakerRuntimeState.Open => false,
                CircuitBreakerRuntimeState.HalfOpen => state.HalfOpenInFlight < source.Policy.HalfOpenMaxAttempts,
                _ => true
            };
        }
    }

    public void RecordRejectedIfUnavailable(CircuitBreakerStatusSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Policy.Enabled)
        {
            return;
        }

        var state = GetOrCreate(source.UpstreamIdentity);
        lock (state.Gate)
        {
            RefreshOpenState(source.Policy, state, _timeProvider.GetUtcNow());
            if (state.State == CircuitBreakerRuntimeState.Open || (state.State == CircuitBreakerRuntimeState.HalfOpen && state.HalfOpenInFlight >= source.Policy.HalfOpenMaxAttempts))
            {
                state.RecordRejectedRequest();
                _metrics.CircuitRejected();
            }
        }
    }

    public CircuitBreakerAcquisitionResult Acquire(CircuitBreakerStatusSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Policy.Enabled)
        {
            return CircuitBreakerAcquisitionResult.Accepted(new CircuitBreakerLease(source, enabled: false, halfOpenProbe: false, _ =>
            {
            }));
        }

        var state = GetOrCreate(source.UpstreamIdentity);
        lock (state.Gate)
        {
            RefreshOpenState(source.Policy, state, _timeProvider.GetUtcNow());
            if (state.State == CircuitBreakerRuntimeState.Open)
            {
                state.RecordRejectedRequest();
                _metrics.CircuitRejected();
                return CircuitBreakerAcquisitionResult.Rejected;
            }

            var halfOpenProbe = state.State == CircuitBreakerRuntimeState.HalfOpen;
            if (halfOpenProbe)
            {
                if (state.HalfOpenInFlight >= source.Policy.HalfOpenMaxAttempts)
                {
                    state.RecordRejectedRequest();
                    _metrics.CircuitRejected();
                    return CircuitBreakerAcquisitionResult.Rejected;
                }

                state.RecordHalfOpenProbeStarted();
            }

            return CircuitBreakerAcquisitionResult.Accepted(new CircuitBreakerLease(source, enabled: true, halfOpenProbe, ReleaseHalfOpenProbe));
        }
    }

    public void RecordSuccess(CircuitBreakerLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!lease.Enabled || !lease.TryComplete())
        {
            return;
        }

        var state = GetOrCreate(lease.Source.UpstreamIdentity);
        lock (state.Gate)
        {
            if (lease.HalfOpenProbe)
            {
                state.RecordHalfOpenProbeCompleted();
                Close(state);
                return;
            }

            state.ClearFailureTracking();
        }
    }

    public void RecordFailure(CircuitBreakerLease lease, string reason, int? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!lease.Enabled || !lease.TryComplete())
        {
            return;
        }

        if (statusCode.HasValue && !ContainsStatus(lease.Source.Policy.FailureStatusCodes, statusCode.Value))
        {
            ReleaseHalfOpenProbe(lease);
            return;
        }

        var state = GetOrCreate(lease.Source.UpstreamIdentity);
        var now = _timeProvider.GetUtcNow();
        lock (state.Gate)
        {
            var normalizedReason = NormalizeReason(reason);
            if (lease.HalfOpenProbe)
            {
                state.RecordFailureReason(normalizedReason);
                state.RecordHalfOpenProbeCompleted();
                Open(state, now);
                return;
            }

            var failureCount = state.RecordFailure(normalizedReason, now, lease.Source.Policy.SamplingWindow);
            if (failureCount >= lease.Source.Policy.FailureThreshold)
            {
                Open(state, now);
            }
        }
    }

    public CircuitBreakerStatus Snapshot(CircuitBreakerStatusSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Policy.Enabled)
        {
            return CircuitBreakerStatus.Disabled(source.Policy);
        }

        var state = GetOrCreate(source.UpstreamIdentity);
        lock (state.Gate)
        {
            RefreshOpenState(source.Policy, state, _timeProvider.GetUtcNow());
            return CircuitBreakerStatus.FromEnabledPolicyState(source.Policy, state.State, state.OpenedAtUtc, state.FailureCount, state.RejectedRequests, state.LastFailureReason);
        }
    }

    private MutableCircuitState GetOrCreate(string upstreamIdentity)
    {
        return _states.GetOrAdd(upstreamIdentity, _ => new MutableCircuitState());
    }
}
