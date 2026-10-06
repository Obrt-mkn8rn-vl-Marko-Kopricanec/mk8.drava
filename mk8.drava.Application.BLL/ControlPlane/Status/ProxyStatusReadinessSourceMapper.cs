using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyStatusReadinessSourceMapper
{
    public static ProxyStatusReadinessSourceSet FromSources(ProxyStatusReadinessConfigurationSourceSet configuration, ProxyStatusRuntimeSummary runtime, ProxyMetricsSnapshot metrics, IReadOnlyList<ProxyUpstreamStatus> upstreams, RuntimeHttp3SupportProjection http3, ProxyLogPersistenceStatus logPersistence)
    {
        ArgumentNullException.ThrowIfNull(http3);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(metrics);
        return new ProxyStatusReadinessSourceSet(configuration.HasActiveConfiguration, configuration.ConfigGeneration, configuration.ConfigurationLoadedAtUtc, runtime.LastListenerReload is ProxyListenerReloadResult.AppliedResult, runtime.LastListenerReload is ProxyListenerReloadResult.FailedResult, configuration.ConfiguredListeners, ProxyRuntimeListenerSummarySourceMapper.FromSources(runtime.Listeners), configuration.Routes, configuration.Certificates, configuration.Acme, ProxyUpstreamSummarySourceMapper.FromStatusResponses(upstreams), configuration.LimitConfiguration, ProxyLimitSummarySourceMapper.FromSources(metrics.ClientConnections.Active, metrics.Tls.ActiveHandshakes, metrics.Http2.ActiveStreams, metrics.Http3.ActiveStreams, metrics.UpstreamHttp3.ActiveStreams), http3.EnabledForTraffic, http3.QuicListenerReady, ProxyLogSummarySourceMapper.FromStatus(logPersistence), ProxyShutdownSummarySourceMapper.FromSources(runtime.ListenerLive, runtime.IsShuttingDown, runtime.ShutdownStartedAtUtc, runtime.ShutdownDeadlineUtc));
    }
}
