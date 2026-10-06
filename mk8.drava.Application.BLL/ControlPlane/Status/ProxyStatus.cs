using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyStatus
{
    public ProxyStatus(bool listenerLive, string? listenerName, string? endpoint, DateTimeOffset? startedAt, DateTimeOffset? stoppedAt, string? lastError, bool isShuttingDown, DateTimeOffset? shutdownStartedAtUtc, DateTimeOffset? shutdownDeadlineUtc, int? configVersion, DateTimeOffset? configLoadedAtUtc, int configuredListeners, int configuredRoutes, ProxyMetricsSnapshot metrics, IReadOnlyList<ProxyUpstreamStatus> upstreams) : this(listenerLive, listenerName, endpoint, startedAt, stoppedAt, lastError, isShuttingDown, shutdownStartedAtUtc, shutdownDeadlineUtc, configVersion, configLoadedAtUtc, configuredListeners, configuredRoutes, metrics, upstreams, listeners: [], lastListenerReload: null, http3: UnknownHttp3Support(), routeDiagnostics: RouteDiagnosticsStatus.Enabled, configLint: ConfigLintStatus.Empty, logPersistence: ProxyLogPersistenceStatus.Unknown, readiness: ProxyReadinessStatus.Unknown, subsystems: ProxySubsystemSummaries.Unknown, runtimePreflight: ProxyRuntimePreflightStatus.Unknown)
    {
    }

    public ProxyStatus(bool listenerLive, string? listenerName, string? endpoint, DateTimeOffset? startedAt, DateTimeOffset? stoppedAt, string? lastError, bool isShuttingDown, DateTimeOffset? shutdownStartedAtUtc, DateTimeOffset? shutdownDeadlineUtc, int? configVersion, DateTimeOffset? configLoadedAtUtc, int configuredListeners, int configuredRoutes, ProxyMetricsSnapshot metrics, IReadOnlyList<ProxyUpstreamStatus> upstreams, IReadOnlyList<ProxyListenerStatus> listeners, ProxyListenerReloadResult? lastListenerReload, RuntimeHttp3SupportProjection http3, RouteDiagnosticsStatus routeDiagnostics, ConfigLintStatus configLint, ProxyLogPersistenceStatus logPersistence, ProxyReadinessStatus readiness, ProxySubsystemSummaries subsystems, ProxyRuntimePreflightStatus runtimePreflight)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(upstreams);
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(http3);
        ArgumentNullException.ThrowIfNull(routeDiagnostics);
        ArgumentNullException.ThrowIfNull(configLint);
        ArgumentNullException.ThrowIfNull(logPersistence);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(subsystems);
        ArgumentNullException.ThrowIfNull(runtimePreflight);
        ProxyStatusFacts.RequireShutdownWindow(isShuttingDown, shutdownStartedAtUtc, nameof(shutdownStartedAtUtc), shutdownDeadlineUtc, nameof(shutdownDeadlineUtc));
        ProxyStatusFacts.RequireOptionalNonNegative(configVersion, nameof(configVersion));
        ProxyStatusFacts.RequireNonNegative(configuredListeners, nameof(configuredListeners));
        ProxyStatusFacts.RequireNonNegative(configuredRoutes, nameof(configuredRoutes));
        ListenerLive = listenerLive;
        ListenerName = listenerName;
        Endpoint = endpoint;
        StartedAt = startedAt;
        StoppedAt = stoppedAt;
        LastError = lastError;
        IsShuttingDown = isShuttingDown;
        ShutdownStartedAtUtc = shutdownStartedAtUtc;
        ShutdownDeadlineUtc = shutdownDeadlineUtc;
        ConfigVersion = configVersion;
        ConfigLoadedAtUtc = configLoadedAtUtc;
        ConfiguredListeners = configuredListeners;
        ConfiguredRoutes = configuredRoutes;
        Metrics = metrics;
        Upstreams = ProxyStatusList.Copy(upstreams);
        Listeners = ProxyStatusList.Copy(listeners);
        LastListenerReload = lastListenerReload;
        Http3 = http3;
        RouteDiagnostics = routeDiagnostics;
        ConfigLint = configLint;
        LogPersistence = logPersistence;
        Readiness = readiness;
        Subsystems = subsystems;
        RuntimePreflight = runtimePreflight;
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
    public int? ConfigVersion { get; }
    public DateTimeOffset? ConfigLoadedAtUtc { get; }
    public int ConfiguredListeners { get; }
    public int ConfiguredRoutes { get; }
    public ProxyMetricsSnapshot Metrics { get; }
    public IReadOnlyList<ProxyUpstreamStatus> Upstreams { get; }
    public IReadOnlyList<ProxyListenerStatus> Listeners { get; }
    public ProxyListenerReloadResult? LastListenerReload { get; }
    public RuntimeHttp3SupportProjection Http3 { get; }
    public RouteDiagnosticsStatus RouteDiagnostics { get; }
    public ConfigLintStatus ConfigLint { get; }
    public ProxyLogPersistenceStatus LogPersistence { get; }
    public ProxyReadinessStatus Readiness { get; }
    public ProxySubsystemSummaries Subsystems { get; }
    public ProxyRuntimePreflightStatus RuntimePreflight { get; }

    private static RuntimeHttp3SupportProjection UnknownHttp3Support()
    {
        return new RuntimeHttp3SupportProjection("unknown", QuicListenerSupported: false, QuicConnectionSupported: false, "disabled", "disabled", EnabledForTraffic: false, QuicListenerReady: false, AltSvcConfigured: false, AltSvcActive: false, AltSvcMaxAgeSeconds: null, "not_configured", UdpQuicListenerIdentityModeled: true, "client_http3_default_enabled_for_eligible_tls_proxy_listeners");
    }
}
