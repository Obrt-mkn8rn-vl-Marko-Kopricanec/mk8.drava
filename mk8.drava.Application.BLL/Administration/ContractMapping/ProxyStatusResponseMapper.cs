using BusinessProxyStatus = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyStatusResponseMapper
{
    public static ProxyStatusResponse FromBusinessResponse(BusinessProxyStatus response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new ProxyStatusResponse(listenerLive: response.ListenerLive, listenerName: response.ListenerName, endpoint: response.Endpoint, startedAt: response.StartedAt, stoppedAt: response.StoppedAt, lastError: response.LastError, isShuttingDown: response.IsShuttingDown, shutdownStartedAtUtc: response.ShutdownStartedAtUtc, shutdownDeadlineUtc: response.ShutdownDeadlineUtc, configVersion: response.ConfigVersion, configLoadedAtUtc: response.ConfigLoadedAtUtc, configuredListeners: response.ConfiguredListeners, configuredRoutes: response.ConfiguredRoutes, metrics: ProxyMetricsSnapshotResponseMapper.FromSnapshot(response.Metrics), upstreams: ProxyUpstreamStatusResponseMapper.FromStatuses(response.Upstreams), listeners: ProxyListenerStatusResponseMapper.FromStatuses(response.Listeners), lastListenerReload: response.LastListenerReload is null ? null : ProxyListenerReloadResponseMapper.FromResult(response.LastListenerReload), http3: RuntimeHttp3SupportResponseMapper.FromProjection(response.Http3), routeDiagnostics: RouteDiagnosticsStatusResponseMapper.FromStatus(response.RouteDiagnostics), configLint: ConfigLintStatusResponseMapper.FromStatus(response.ConfigLint), logPersistence: ProxyLogPersistenceStatusResponseMapper.FromStatus(response.LogPersistence), readiness: ProxyReadinessStatusResponseMapper.FromStatus(response.Readiness), subsystems: ProxySubsystemSummariesResponseMapper.FromSummaries(response.Subsystems), runtimePreflight: ProxyRuntimePreflightStatusResponseMapper.FromStatus(response.RuntimePreflight));
    }
}
