using Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyStatusBuilder
{
    public static ProxyStatus Build(ProxyStatusInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var configuration = input.Configuration;
        var runtime = input.Runtime;
        var listenerCount = configuration?.ListenerCount ?? 0;
        var routeCount = configuration?.RouteCount ?? 0;
        var(readiness, subsystems) = ProxyStatusReadinessBuilder.Build(input.Readiness);
        return new ProxyStatus(runtime.ListenerLive, runtime.ListenerName, runtime.Endpoint, runtime.StartedAt, runtime.StoppedAt, runtime.LastError, runtime.IsShuttingDown, runtime.ShutdownStartedAtUtc, runtime.ShutdownDeadlineUtc, configuration?.Version, configuration?.LoadedAtUtc, listenerCount, routeCount, input.Metrics, input.Upstreams, runtime.Listeners, runtime.LastListenerReload, input.Http3, RouteDiagnosticsStatus.Enabled, input.ConfigLint, input.LogPersistence, readiness, subsystems, input.RuntimePreflight);
    }
}
