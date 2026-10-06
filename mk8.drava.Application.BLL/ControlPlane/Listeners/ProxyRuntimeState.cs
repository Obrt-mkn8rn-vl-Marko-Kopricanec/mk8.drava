using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public sealed class ProxyRuntimeState : IProxyStatusRuntimeStateSource, IHttp3AltSvcRuntimeListenerSource
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _timeProvider;
    private int _isRunning;
    private string? _listenerName;
    private string? _endpoint;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _stoppedAt;
    private string? _lastError;
    private int _isShuttingDown;
    private DateTimeOffset? _shutdownStartedAtUtc;
    private DateTimeOffset? _shutdownDeadlineUtc;
    private ProxyListenerStatus[] _listeners = [];
    private ProxyListenerReloadResult? _lastListenerReload;
    public ProxyRuntimeState(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public ProxyRuntimeSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new ProxyRuntimeSnapshot(Volatile.Read(ref _isRunning) == 1, _listenerName, _endpoint, _startedAt, _stoppedAt, _lastError, Volatile.Read(ref _isShuttingDown) == 1, _shutdownStartedAtUtc, _shutdownDeadlineUtc, _listeners, _lastListenerReload);
        }
    }

    public ProxyStatusRuntimeSummary ReadRuntimeSummary()
    {
        var snapshot = Snapshot();
        return ProxyStatusRuntimeSummaryMapper.FromSources(snapshot.IsRunning, snapshot.ListenerName, snapshot.Endpoint, snapshot.StartedAt, snapshot.StoppedAt, snapshot.LastError, snapshot.IsShuttingDown, snapshot.ShutdownStartedAtUtc, snapshot.ShutdownDeadlineUtc, snapshot.Listeners, snapshot.LastListenerReload);
    }

    public IReadOnlyList<ProxyListenerStatus> ReadRuntimeListeners()
    {
        lock (_gate)
        {
            return ProxyListenerList.Copy(_listeners);
        }
    }

    public void MarkShuttingDown(DateTimeOffset startedAtUtc, DateTimeOffset deadlineUtc)
    {
        lock (_gate)
        {
            _shutdownStartedAtUtc = startedAtUtc;
            _shutdownDeadlineUtc = deadlineUtc;
            Volatile.Write(ref _isShuttingDown, 1);
        }
    }

    public void ReplaceListeners(IEnumerable<ProxyListenerStatus> listeners, ProxyListenerReloadResult? lastReload)
    {
        var listenerSnapshot = ProxyListenerList.Copy(listeners).ToArray();
        lock (_gate)
        {
            _listeners = listenerSnapshot;
            _lastListenerReload = lastReload ?? _lastListenerReload;
            var active = listenerSnapshot.FirstOrDefault(static listener => listener.State == ProxyListenerState.Active);
            if (active is not null)
            {
                _listenerName = active.Name;
                _endpoint = $"{active.Address}:{active.Port}";
                _startedAt = active.StartedAtUtc;
                _stoppedAt = null;
                _lastError = null;
                Volatile.Write(ref _isRunning, 1);
                Volatile.Write(ref _isShuttingDown, 0);
                _shutdownStartedAtUtc = null;
                _shutdownDeadlineUtc = null;
                return;
            }

            _listenerName = null;
            _endpoint = null;
            _startedAt = null;
            _stoppedAt = _timeProvider.GetUtcNow();
            _lastError = listenerSnapshot.Length == 0 ? "No configured proxy listener." : listenerSnapshot.FirstOrDefault(static listener => listener.State == ProxyListenerState.Failed)?.LastError;
            Volatile.Write(ref _isRunning, 0);
        }
    }
}
