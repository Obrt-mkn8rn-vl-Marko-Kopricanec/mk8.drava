using System.Collections.ObjectModel;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyStatusRuntimeSummary
{
    public ProxyStatusRuntimeSummary(bool ListenerLive, string? ListenerName, string? Endpoint, DateTimeOffset? StartedAt, DateTimeOffset? StoppedAt, string? LastError, bool IsShuttingDown, DateTimeOffset? ShutdownStartedAtUtc, DateTimeOffset? ShutdownDeadlineUtc, IReadOnlyList<ProxyListenerStatus> Listeners, ProxyListenerReloadResult? LastListenerReload)
    {
        ArgumentNullException.ThrowIfNull(Listeners);
        this.ListenerLive = ListenerLive;
        this.ListenerName = ListenerName;
        this.Endpoint = Endpoint;
        this.StartedAt = StartedAt;
        this.StoppedAt = StoppedAt;
        this.LastError = LastError;
        this.IsShuttingDown = IsShuttingDown;
        this.ShutdownStartedAtUtc = ShutdownStartedAtUtc;
        this.ShutdownDeadlineUtc = ShutdownDeadlineUtc;
        this.Listeners = ProxyStatusList.Copy(Listeners);
        this.LastListenerReload = LastListenerReload;
    }

    public bool ListenerLive { get; }
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
