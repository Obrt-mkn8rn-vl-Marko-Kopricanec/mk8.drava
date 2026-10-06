namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public sealed record ProxyRuntimeSnapshot
{
    public ProxyRuntimeSnapshot(bool IsRunning, string? ListenerName, string? Endpoint, DateTimeOffset? StartedAt, DateTimeOffset? StoppedAt, string? LastError, bool IsShuttingDown = false, DateTimeOffset? ShutdownStartedAtUtc = null, DateTimeOffset? ShutdownDeadlineUtc = null) : this(IsRunning, ListenerName, Endpoint, StartedAt, StoppedAt, LastError, IsShuttingDown, ShutdownStartedAtUtc, ShutdownDeadlineUtc, listeners: [], lastListenerReload: null)
    {
    }

    public ProxyRuntimeSnapshot(bool isRunning, string? listenerName, string? endpoint, DateTimeOffset? startedAt, DateTimeOffset? stoppedAt, string? lastError, bool isShuttingDown, DateTimeOffset? shutdownStartedAtUtc, DateTimeOffset? shutdownDeadlineUtc, IReadOnlyList<ProxyListenerStatus> listeners, ProxyListenerReloadResult? lastListenerReload)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        IsRunning = isRunning;
        ListenerName = listenerName;
        Endpoint = endpoint;
        StartedAt = startedAt;
        StoppedAt = stoppedAt;
        LastError = lastError;
        IsShuttingDown = isShuttingDown;
        ShutdownStartedAtUtc = shutdownStartedAtUtc;
        ShutdownDeadlineUtc = shutdownDeadlineUtc;
        Listeners = ProxyListenerList.Copy(listeners);
        LastListenerReload = lastListenerReload;
    }

    public bool IsRunning { get; }
    public string? ListenerName { get; }
    public string? Endpoint { get; }
    public DateTimeOffset? StartedAt { get; }
    public DateTimeOffset? StoppedAt { get; }
    public string? LastError { get; }
    public bool IsShuttingDown { get; }
    public DateTimeOffset? ShutdownStartedAtUtc { get; }
    public DateTimeOffset? ShutdownDeadlineUtc { get; }
    public IReadOnlyList<ProxyListenerStatus> Listeners { get; }
    public ProxyListenerReloadResult? LastListenerReload { get; }
}
