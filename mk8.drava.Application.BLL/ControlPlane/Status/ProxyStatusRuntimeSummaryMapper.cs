using System.Collections.ObjectModel;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyStatusRuntimeSummaryMapper
{
    public static ProxyStatusRuntimeSummary FromSources(bool listenerLive, string? listenerName, string? endpoint, DateTimeOffset? startedAt, DateTimeOffset? stoppedAt, string? lastError, bool isShuttingDown, DateTimeOffset? shutdownStartedAtUtc, DateTimeOffset? shutdownDeadlineUtc, IReadOnlyList<ProxyListenerStatus> listeners, ProxyListenerReloadResult? lastListenerReload)
    {
        return new ProxyStatusRuntimeSummary(listenerLive, listenerName, endpoint, startedAt, stoppedAt, lastError, isShuttingDown, shutdownStartedAtUtc, shutdownDeadlineUtc, listeners, lastListenerReload);
    }
}
