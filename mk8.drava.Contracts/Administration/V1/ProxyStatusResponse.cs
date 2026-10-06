namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyStatusResponse
{
    public ProxyStatusResponse(bool listenerLive, string? listenerName, string? endpoint, DateTimeOffset? startedAt, DateTimeOffset? stoppedAt, string? lastError, bool isShuttingDown, DateTimeOffset? shutdownStartedAtUtc, DateTimeOffset? shutdownDeadlineUtc, int? configVersion, DateTimeOffset? configLoadedAtUtc, int configuredListeners, int configuredRoutes, ProxyMetricsSnapshotResponse metrics, IReadOnlyList<ProxyUpstreamStatusResponse> upstreams, IReadOnlyList<ProxyListenerStatusResponse> listeners, ProxyListenerReloadResponse? lastListenerReload, RuntimeHttp3SupportResponse http3, RouteDiagnosticsStatusResponse routeDiagnostics, ConfigLintStatusResponse configLint, ProxyLogPersistenceStatusResponse logPersistence, ProxyReadinessStatusResponse readiness, ProxySubsystemSummariesResponse subsystems, ProxyRuntimePreflightStatusResponse runtimePreflight)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(http3);
        ArgumentNullException.ThrowIfNull(routeDiagnostics);
        ArgumentNullException.ThrowIfNull(configLint);
        ArgumentNullException.ThrowIfNull(logPersistence);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(subsystems);
        ArgumentNullException.ThrowIfNull(runtimePreflight);
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
        Upstreams = ApiResponseList.Copy(upstreams);
        Listeners = ApiResponseList.Copy(listeners);
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
    public ProxyMetricsSnapshotResponse Metrics { get; }
    public IReadOnlyList<ProxyUpstreamStatusResponse> Upstreams { get; }
    public IReadOnlyList<ProxyListenerStatusResponse> Listeners { get; }
    public ProxyListenerReloadResponse? LastListenerReload { get; }
    public RuntimeHttp3SupportResponse Http3 { get; }
    public RouteDiagnosticsStatusResponse RouteDiagnostics { get; }
    public ConfigLintStatusResponse ConfigLint { get; }
    public ProxyLogPersistenceStatusResponse LogPersistence { get; }
    public ProxyReadinessStatusResponse Readiness { get; }
    public ProxySubsystemSummariesResponse Subsystems { get; }
    public ProxyRuntimePreflightStatusResponse RuntimePreflight { get; }
}
