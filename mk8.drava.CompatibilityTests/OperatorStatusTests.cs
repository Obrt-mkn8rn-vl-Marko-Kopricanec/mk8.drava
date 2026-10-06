using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Connections;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;
using Mk8.Drava.Application.DAL.Observability;
using Mk8.Drava.Application.INF.Observability;
using Mk8.Drava.Application.DAL.Observability;
using Mk8.Drava.Application.INF.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class OperatorStatusTests
{
    private const string AdminToken = "phase-49-admin-token";
    public static void StatusReadinessReportsHealthyForActiveSnapshot()
    {
        using var fixture = StatusFixture.Create();
        var listener = Listener();
        fixture.Store.Replace(Snapshot([listener], [StaticRoute(cache: CachePolicy())]));
        fixture.Runtime.ReplaceListeners([ListenerStatus(listener, ProxyListenerState.Active)], null);
        var status = fixture.Controller().Get();
        AssertEx.Equal("healthy", status.Readiness.State);
        AssertEx.Equal(0, status.Readiness.Reasons.Count);
        AssertEx.Equal(1, status.ConfigVersion);
        AssertEx.Equal(1, status.Subsystems.Listeners.Active);
        AssertEx.Equal(1, status.Subsystems.Routes.CacheEnabledRoutes);
        AssertEx.True(status.Subsystems.Cache.Enabled);
        AssertEx.Equal(4096, status.Subsystems.Limits.MaxActiveClientConnections);
        AssertEx.Equal("healthy", status.Subsystems.Logs.State);
    }

    public static void StatusInputReaderAssemblesNamedInputFromSources()
    {
        using var fixture = StatusFixture.Create();
        var listener = Listener();
        var upstream = Upstream();
        var route = ProxyRoute(upstream, healthEnabled: true);
        fixture.Store.Replace(Snapshot([listener], [route]));
        fixture.Runtime.ReplaceListeners([ListenerStatus(listener, ProxyListenerState.Active)], null);
        fixture.Health.RecordHealthCheckResult(HealthTarget(route, upstream), HealthCheckSample.HealthyResult("status_200"), DateTimeOffset.UtcNow);
        var observedAtUtc = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);
        var input = fixture.InputReader(timeProvider: new FixedTimeProvider(observedAtUtc)).Read();
        AssertEx.NotNull(input.Configuration);
        AssertEx.Equal(1, input.Configuration!.Version);
        AssertEx.Equal(1, input.Runtime.Listeners.Count);
        AssertEx.Equal(1, input.Upstreams.Count);
        AssertEx.Equal(UpstreamHealthState.Healthy, input.Upstreams[0].HealthState);
        AssertEx.Equal(ProxyStatusText.Healthy, input.LogPersistence.State);
        AssertEx.Equal(ProxyRuntimePreflightStatus.Unknown, input.RuntimePreflight);
        AssertEx.Equal(observedAtUtc, input.ObservedAtUtc);
        AssertEx.True(input.ConfigLint.Available);
    }

    public static void StatusUpstreamHealthReaderConsumesConfiguredHealthSourcesWithoutRuntimeRoutes()
    {
        var healthSource = new ProxyUpstreamHealthSource(new UpstreamHealthStateSource("site-a/upstream-a", "site-a", "upstream-a", "https://upstream.internal:443"), new CircuitBreakerStatusSource("site-a/upstream-a", CircuitBreakerPolicyInput.Disabled), "https", RuntimeUpstreamProtocol.Http2, 7, ValidateCertificate: false, EffectiveSniHost: "sni.internal", HealthCheckEnabled: true);
        var replacementHealthSource = healthSource with
        {
            HealthState = healthSource.HealthState with
            {
                UpstreamIdentity = "site-b/upstream-b",
                RouteName = "site-b",
                UpstreamName = "upstream-b"
            },
            Protocol = RuntimeUpstreamProtocol.Http3,
            Weight = 99
        };
        var healthSources = new List<ProxyUpstreamHealthSource>
        {
            healthSource
        };
        var configuration = new ProxyStatusConfigurationSourceSet(ConfigurationSummary: new ProxyStatusConfigurationSummary(9, DateTimeOffset.UnixEpoch, ListenerCount: 0, RouteCount: 0), UpstreamHealthSources: healthSources, Http3Configuration: Http3SupportConfigurationSource.Empty, ReadinessConfiguration: ProxyStatusReadinessConfigurationSourceSet.Missing);
        var upstreamHealthSource = new CapturingStatusUpstreamHealthSource();
        var reader = new ProxyStatusUpstreamHealthReader(new FixedStatusConfigurationSource(configuration), upstreamHealthSource);
        healthSources[0] = replacementHealthSource;
        healthSources.Clear();
        var upstreams = reader.ReadUpstreams();
        AssertEx.False(configuration.UpstreamHealthSources is ProxyUpstreamHealthSource[], "Status configuration upstream sources should not expose a mutable array.");
        AssertEx.Equal(1, upstreamHealthSource.LastUpstreams.Count);
        AssertEx.Equal("site-a/upstream-a", upstreamHealthSource.LastUpstreams[0].HealthState.UpstreamIdentity);
        AssertEx.Equal(RuntimeUpstreamProtocol.Http2, upstreamHealthSource.LastUpstreams[0].Protocol);
        AssertEx.False(upstreamHealthSource.LastUpstreams[0].ValidateCertificate);
        AssertEx.Equal("sni.internal", upstreamHealthSource.LastUpstreams[0].EffectiveSniHost);
        AssertEx.Equal(1, upstreams.Count);
        AssertEx.Equal("site-a", upstreams[0].RouteName);
        AssertEx.Equal("upstream-a", upstreams[0].UpstreamName);
    }

    public static void StatusRuntimeSummaryMapperProjectsOnlyResponseRuntimeFacts()
    {
        var listener = Listener();
        var startedAt = new DateTimeOffset(2026, 6, 10, 9, 10, 0, TimeSpan.Zero);
        var stoppedAt = startedAt.AddMinutes(2);
        var shutdownStartedAt = startedAt.AddMinutes(3);
        var shutdownDeadline = startedAt.AddMinutes(4);
        var reload = ProxyListenerReloadResult.Failed(startedAt, added: 1, removed: 0, changed: 0, unchanged: 0, changes: [], errors: ["bind_failed"]);
        var failedListener = ListenerStatus(listener, ProxyListenerState.Failed);
        var activeReplacement = ListenerStatus(listener, ProxyListenerState.Active);
        var runtimeListeners = new List<ProxyListenerStatus>
        {
            failedListener
        };
        var runtime = new ProxyRuntimeSnapshot(isRunning: false, listenerName: "main", endpoint: "127.0.0.1:18080", startedAt: startedAt, stoppedAt: stoppedAt, lastError: "listener failed", isShuttingDown: true, shutdownStartedAtUtc: shutdownStartedAt, shutdownDeadlineUtc: shutdownDeadline, listeners: runtimeListeners, lastListenerReload: reload);
        runtimeListeners[0] = activeReplacement;
        runtimeListeners.Clear();
        var summary = ProxyStatusRuntimeSummaryMapper.FromSources(runtime.IsRunning, runtime.ListenerName, runtime.Endpoint, runtime.StartedAt, runtime.StoppedAt, runtime.LastError, runtime.IsShuttingDown, runtime.ShutdownStartedAtUtc, runtime.ShutdownDeadlineUtc, runtime.Listeners, runtime.LastListenerReload);
        AssertEx.False(summary.ListenerLive);
        AssertEx.Equal("main", summary.ListenerName);
        AssertEx.Equal("127.0.0.1:18080", summary.Endpoint);
        AssertEx.Equal(startedAt, summary.StartedAt);
        AssertEx.Equal(stoppedAt, summary.StoppedAt);
        AssertEx.Equal("listener failed", summary.LastError);
        AssertEx.True(summary.IsShuttingDown);
        AssertEx.Equal(shutdownStartedAt, summary.ShutdownStartedAtUtc);
        AssertEx.Equal(shutdownDeadline, summary.ShutdownDeadlineUtc);
        AssertEx.Equal(1, summary.Listeners.Count);
        AssertEx.Equal(ProxyListenerState.Failed, summary.Listeners[0].State);
        AssertEx.Equal(reload, summary.LastListenerReload);
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRuntimeSnapshot(isRunning: false, listenerName: "main", endpoint: "127.0.0.1:18080", startedAt: startedAt, stoppedAt: stoppedAt, lastError: "listener failed", isShuttingDown: true, shutdownStartedAtUtc: shutdownStartedAt, shutdownDeadlineUtc: shutdownDeadline, listeners: null!, lastListenerReload: reload));
    }

    public static void StatusRuntimeSourceReadsRuntimeSummaryWithoutRuntimeSnapshot()
    {
        var listener = Listener();
        var runtime = new ProxyRuntimeState(TimeProvider.System);
        runtime.ReplaceListeners([ListenerStatus(listener, ProxyListenerState.Active)], null);
        IProxyStatusRuntimeStateSource source = runtime;
        var summary = source.ReadRuntimeSummary();
        AssertEx.True(summary.ListenerLive);
        AssertEx.Equal(listener.Name, summary.ListenerName);
        AssertEx.Equal($"{listener.Address}:{listener.Port}", summary.Endpoint);
        AssertEx.Equal(1, summary.Listeners.Count);
        AssertEx.Equal(ProxyListenerState.Active, summary.Listeners[0].State);
    }

    public static void RuntimeStateCopiesListenerListsOnWriteAndRead()
    {
        var listener = Listener();
        var active = ListenerStatus(listener, ProxyListenerState.Active);
        var failed = ListenerStatus(listener, ProxyListenerState.Failed);
        var listeners = new List<ProxyListenerStatus>
        {
            active
        };
        var runtime = new ProxyRuntimeState(TimeProvider.System);
        runtime.ReplaceListeners(listeners.Select(static listener => listener), null);
        listeners.Clear();
        var afterInputMutation = runtime.Snapshot();
        AssertEx.Equal(1, afterInputMutation.Listeners.Count);
        AssertEx.Equal(ProxyListenerState.Active, afterInputMutation.Listeners[0].State);
        AssertEx.Throws<ArgumentNullException>(() => runtime.ReplaceListeners([null!], null));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRuntimeSnapshot(isRunning: true, listenerName: "main", endpoint: "127.0.0.1:18080", startedAt: DateTimeOffset.UnixEpoch, stoppedAt: null, lastError: null, isShuttingDown: false, shutdownStartedAtUtc: null, shutdownDeadlineUtc: null, listeners: [null!], lastListenerReload: null));
        var readListeners = runtime.ReadRuntimeListeners();
        AssertEx.Equal(1, readListeners.Count);
        AssertEx.False(readListeners is ProxyListenerStatus[], "Runtime listener reads should not expose a mutable array.");
        var responseListeners = ProxyListenerStatusResponseMapper.FromStatuses(readListeners);
        AssertEx.False(responseListeners is ProxyListenerStatusResponse[], "Listener status API responses should not expose a mutable array.");
        if (readListeners is ProxyListenerStatus[] readArray)
        {
            readArray[0] = failed;
        }

        var afterReadMutation = runtime.Snapshot();
        AssertEx.Equal(1, afterReadMutation.Listeners.Count);
        AssertEx.Equal(ProxyListenerState.Active, afterReadMutation.Listeners[0].State);
        var snapshotListeners = afterReadMutation.Listeners;
        AssertEx.False(snapshotListeners is ProxyListenerStatus[], "Runtime snapshot listeners should not expose a mutable array.");
        if (snapshotListeners is ProxyListenerStatus[] snapshotArray)
        {
            snapshotArray[0] = failed;
        }

        var afterSnapshotMutation = runtime.Snapshot();
        AssertEx.Equal(1, afterSnapshotMutation.Listeners.Count);
        AssertEx.Equal(ProxyListenerState.Active, afterSnapshotMutation.Listeners[0].State);
    }

    public static void StatusConfigurationSummaryMapperReadsCountsWithoutRuntimeConfigurationObjects()
    {
        var loadedAtUtc = new DateTimeOffset(2026, 6, 12, 8, 0, 0, TimeSpan.Zero);
        var summary = ProxyStatusConfigurationSummaryMapper.FromCounts(version: 17, loadedAtUtc, listenerCount: 2, routeCount: 3);
        AssertEx.Equal(17, summary.Version);
        AssertEx.Equal(loadedAtUtc, summary.LoadedAtUtc);
        AssertEx.Equal(2, summary.ListenerCount);
        AssertEx.Equal(3, summary.RouteCount);
    }

    public static void StatusConfigurationNumericFactsRejectNegativeValues()
    {
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyStatusConfigurationSummaryMapper.FromCounts(version: -1, DateTimeOffset.UnixEpoch, listenerCount: 0, routeCount: 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyStatusConfigurationSummaryMapper.FromCounts(version: 1, DateTimeOffset.UnixEpoch, listenerCount: -1, routeCount: 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyStatusConfigurationSummaryMapper.FromCounts(version: 1, DateTimeOffset.UnixEpoch, listenerCount: 0, routeCount: -1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyConfigSubsystemSummary(Active: true, Generation: -1, LoadedAtUtc: DateTimeOffset.UnixEpoch, LastListenerReloadSucceeded: true, LastListenerReloadReason: "listener_reload_succeeded"));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyStatusReadinessConfigurationSourceSet(HasActiveConfiguration: true, ConfigGeneration: -1, ConfigurationLoadedAtUtc: DateTimeOffset.UnixEpoch, ConfiguredListeners: [], Routes: [], Certificates: null, Acme: null, LimitConfiguration: null));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => BuildReadinessSourceSet(configGeneration: -1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => BuildReadinessInput(configGeneration: -1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyReadinessEvaluationInput(HasActiveConfiguration: true, ConfigGeneration: -1, IsShuttingDown: false, LastListenerReloadFailed: false, LogPersistenceState: ProxyStatusText.Healthy, RuntimePreflight: ProxyRuntimePreflightStatus.Unknown, Subsystems: HealthyReadinessSubsystems(), EvaluatedAtUtc: DateTimeOffset.UnixEpoch));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyReadinessStatus.Evaluated(ProxyStatusText.Healthy, [], DateTimeOffset.UnixEpoch, configGeneration: -1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => BuildProxyStatus(configVersion: -1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => BuildProxyStatus(configuredListeners: -1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => BuildProxyStatus(configuredRoutes: -1));
        var noConfig = BuildReadinessInput(configGeneration: null);
        AssertEx.Equal(null, noConfig.ConfigGeneration);
        static ProxyStatusReadinessSourceSet BuildReadinessSourceSet(int? configGeneration)
        {
            return new ProxyStatusReadinessSourceSet(HasActiveConfiguration: configGeneration is not null, ConfigGeneration: configGeneration, ConfigurationLoadedAtUtc: configGeneration is null ? null : DateTimeOffset.UnixEpoch, LastListenerReloadSucceeded: null, LastListenerReloadFailed: false, ConfiguredListeners: [], RuntimeListeners: [], Routes: [], Certificates: null, Acme: null, Upstreams: [], LimitConfiguration: null, LimitRuntime: new ProxyLimitRuntimeSummarySource(ActiveConnections: 0, ActiveTlsHandshakes: 0, ActiveHttp2Streams: 0, ActiveHttp3Streams: 0, ActiveUpstreamHttp3Streams: 0), ClientHttp3Enabled: false, ClientHttp3Ready: false, Log: new ProxyLogSummarySource(AccessLogPersistenceEnabled: false, AdminAuditPersistenceEnabled: false, State: ProxyStatusText.Unknown, Reason: ProxyStatusText.NotAvailable), Shutdown: new ProxyShutdownSummarySource(IsRunning: false, IsShuttingDown: false, ShutdownStartedAtUtc: null, ShutdownDeadlineUtc: null));
        }

        static ProxyStatusReadinessInput BuildReadinessInput(int? configGeneration)
        {
            return new ProxyStatusReadinessInput(HasActiveConfiguration: configGeneration is not null, ConfigGeneration: configGeneration, ConfigurationLoadedAtUtc: configGeneration is null ? null : DateTimeOffset.UnixEpoch, LastListenerReloadSucceeded: null, LastListenerReloadFailed: false, ConfiguredListeners: [], RuntimeListeners: [], Routes: [], Certificates: null, Acme: null, Upstreams: [], LimitConfiguration: null, LimitRuntime: new ProxyLimitRuntimeSummarySource(ActiveConnections: 0, ActiveTlsHandshakes: 0, ActiveHttp2Streams: 0, ActiveHttp3Streams: 0, ActiveUpstreamHttp3Streams: 0), ClientHttp3Enabled: false, ClientHttp3Ready: false, Log: new ProxyLogSummarySource(AccessLogPersistenceEnabled: false, AdminAuditPersistenceEnabled: false, State: ProxyStatusText.Unknown, Reason: ProxyStatusText.NotAvailable), Shutdown: new ProxyShutdownSummarySource(IsRunning: false, IsShuttingDown: false, ShutdownStartedAtUtc: null, ShutdownDeadlineUtc: null), CacheStatus: null, AcmeStatuses: [], RuntimePreflight: ProxyRuntimePreflightStatus.Unknown, ObservedAtUtc: DateTimeOffset.UnixEpoch);
        }

        static ProxyStatus BuildProxyStatus(int? configVersion = 1, int configuredListeners = 0, int configuredRoutes = 0)
        {
            return new ProxyStatus(listenerLive: false, listenerName: null, endpoint: null, startedAt: null, stoppedAt: DateTimeOffset.UnixEpoch, lastError: null, isShuttingDown: false, shutdownStartedAtUtc: null, shutdownDeadlineUtc: null, configVersion: configVersion, configLoadedAtUtc: DateTimeOffset.UnixEpoch, configuredListeners: configuredListeners, configuredRoutes: configuredRoutes, metrics: new ProxyMetrics().Snapshot(), upstreams: []);
        }
    }

    public static void StatusConfigurationSourceMapperShapesStatusFactsFromRuntimeFactsWithoutSnapshot()
    {
        var listener = Listener();
        var route = StaticRoute();
        var snapshot = Snapshot([listener], [route]);
        var listenerSources = new List<RuntimeListener>
        {
            listener
        };
        var routeSources = new List<RuntimeRoute>
        {
            route
        };
        var certificateSources = new List<RuntimeCertificate>(snapshot.Certificates.Values);
        var source = ProxyStatusConfigurationSourceMapper.FromSources(snapshot.Version, snapshot.LoadedAtUtc, listenerSources.Select(static source => source), routeSources.Select(static source => source), certificateSources.Select(static source => source), snapshot.Acme, snapshot.Limits, ProxyUpstreamHealthSourceMapper.FromRoutes(routeSources));
        listenerSources.Clear();
        routeSources.Clear();
        certificateSources.Clear();
        AssertEx.Equal(snapshot.Version, source.ConfigurationSummary.Version);
        AssertEx.Equal(snapshot.LoadedAtUtc, source.ConfigurationSummary.LoadedAtUtc);
        AssertEx.Equal(1, source.ConfigurationSummary.ListenerCount);
        AssertEx.Equal(1, source.ConfigurationSummary.RouteCount);
        AssertEx.Equal(1, source.Http3Configuration.Listeners.Count);
        AssertEx.False(source.Http3Configuration.UpstreamHttp3Configured);
        AssertEx.True(source.ReadinessConfiguration.HasActiveConfiguration);
        AssertEx.Equal(snapshot.Version, source.ReadinessConfiguration.ConfigGeneration!.Value);
        AssertEx.Equal(snapshot.LoadedAtUtc, source.ReadinessConfiguration.ConfigurationLoadedAtUtc!.Value);
        AssertEx.Equal(1, source.ReadinessConfiguration.ConfiguredListeners.Count);
        AssertEx.Equal(1, source.ReadinessConfiguration.Routes.Count);
        AssertEx.False(source.UpstreamHealthSources is ProxyUpstreamHealthSource[], "Status configuration upstream health sources should not expose a mutable array.");
    }

    public static void StatusResponseBuilderBuildsResponseFromNamedInput()
    {
        var listener = Listener();
        var route = StaticRoute(cache: CachePolicy());
        var snapshot = Snapshot([listener], [route]);
        var runtime = new ProxyRuntimeSnapshot(true, "main", "127.0.0.1:18080", DateTimeOffset.UnixEpoch, null, null, isShuttingDown: false, shutdownStartedAtUtc: null, shutdownDeadlineUtc: null, listeners: [ListenerStatus(listener, ProxyListenerState.Active)], lastListenerReload: null);
        var metrics = new ProxyMetrics().Snapshot();
        var observedAtUtc = new DateTimeOffset(2026, 6, 10, 9, 5, 0, TimeSpan.Zero);
        var runtimeSummary = ProxyStatusRuntimeSummaryMapper.FromSources(runtime.IsRunning, runtime.ListenerName, runtime.Endpoint, runtime.StartedAt, runtime.StoppedAt, runtime.LastError, runtime.IsShuttingDown, runtime.ShutdownStartedAtUtc, runtime.ShutdownDeadlineUtc, runtime.Listeners, runtime.LastListenerReload);
        var http3 = Http3RuntimeSupport.ProjectRuntime(ProxyHttp3SupportConfigurationSourceMapper.FromSources(snapshot.Listeners, snapshot.Routes), TestHttp3PlatformSupport.Supported, Http3SupportSourceMapper.FromListenerStatuses(runtime.Listeners));
        var logPersistence = ProxyLogPersistenceStatus.FromSettings(logDirectory: null, new ProxyLogPersistenceSettings(AccessLogEnabled: true, AdminAuditEnabled: true, MaxFileBytes: 0, MaxFiles: 0), lastSuccessfulWriteAtUtc: null, lastWriteFailure: null);
        var cacheStatus = ProxyCacheStatus.FromSources(entryCount: 3, approximateBytes: 1024, hitCount: 0, missCount: 0, storeCount: 0, evictionCount: 0, storeRejectionCount: 0, lastClearedAtUtc: null, lastClearReason: null, rejections: [], routes: []);
        var preflight = ProxyRuntimePreflightStatus.Unknown;
        var readiness = ProxyStatusReadinessInputMapper.FromSources(ProxyStatusReadinessSourceMapper.FromSources(ProxyStatusReadinessConfigurationSourceMapper.FromSources(snapshot.Version, snapshot.LoadedAtUtc, snapshot.Listeners, snapshot.Routes, snapshot.Certificates.Values, snapshot.Acme, snapshot.Limits), runtimeSummary, metrics, [], http3, logPersistence), cacheStatus, [], preflight, observedAtUtc);
        var input = new ProxyStatusInput(runtimeSummary, ProxyStatusConfigurationSummaryMapper.FromCounts(snapshot.Version, snapshot.LoadedAtUtc, snapshot.Listeners.Count, snapshot.Routes.Count), metrics, [], http3, logPersistence, cacheStatus, [], preflight, observedAtUtc, readiness, ConfigLintStatus.Empty);
        var status = ProxyStatusBuilder.Build(input);
        AssertEx.True(status.ListenerLive);
        AssertEx.Equal("main", status.ListenerName);
        AssertEx.Equal(1, status.ConfiguredListeners);
        AssertEx.Equal(1, status.ConfiguredRoutes);
        AssertEx.Equal(3, status.Subsystems.Cache.EntryCount);
        AssertEx.Equal(1024, status.Subsystems.Cache.ApproximateBytes);
        AssertEx.Equal("healthy", status.Readiness.State);
        AssertEx.Equal(observedAtUtc, status.Readiness.GeneratedAtUtc);
        AssertEx.Equal(ConfigLintStatus.Empty, status.ConfigLint);
    }

    public static void StatusResponseCopiesUpstreamAndListenerLists()
    {
        var listener = Listener();
        var active = ListenerStatus(listener, ProxyListenerState.Active);
        var failed = ListenerStatus(listener, ProxyListenerState.Failed);
        var upstream = new ProxyUpstreamStatus(RouteName: "main", UpstreamName: "primary", Endpoint: "https://primary.internal", Scheme: "https", TlsCertificateValidationEnabled: true, SniHost: "primary.internal", HealthCheckEnabled: true, HealthState: UpstreamHealthState.Healthy, LastHealthCheckResult: "status_200", LastHealthCheckAtUtc: DateTimeOffset.UnixEpoch, ConsecutiveSuccesses: 2, ConsecutiveFailures: 0, SelectedRequests: 11, RequestFailures: 0);
        var replacement = CreateUpstreamStatus(upstream, upstreamName: "replacement");
        var upstreams = new List<ProxyUpstreamStatus>
        {
            upstream
        };
        var listeners = new List<ProxyListenerStatus>
        {
            active
        };
        var status = new ProxyStatus(listenerLive: true, listenerName: "main", endpoint: "127.0.0.1:18080", startedAt: DateTimeOffset.UnixEpoch, stoppedAt: null, lastError: null, isShuttingDown: false, shutdownStartedAtUtc: null, shutdownDeadlineUtc: null, configVersion: 7, configLoadedAtUtc: DateTimeOffset.UnixEpoch, configuredListeners: 1, configuredRoutes: 1, metrics: new ProxyMetrics().Snapshot(), upstreams: upstreams, listeners: listeners, lastListenerReload: null, http3: UnknownHttp3(), routeDiagnostics: RouteDiagnosticsStatus.Enabled, configLint: ConfigLintStatus.Empty, logPersistence: ProxyLogPersistenceStatus.Unknown, readiness: ProxyReadinessStatus.Unknown, subsystems: ProxySubsystemSummaries.Unknown, runtimePreflight: ProxyRuntimePreflightStatus.Unknown);
        upstreams[0] = replacement;
        listeners[0] = failed;
        upstreams.Clear();
        listeners.Clear();
        AssertEx.Equal(1, status.Upstreams.Count);
        AssertEx.Equal("primary", status.Upstreams[0].UpstreamName);
        AssertEx.Equal(RuntimeUpstreamProtocol.Http1, status.Upstreams[0].Protocol);
        AssertEx.Equal(1, status.Upstreams[0].Weight);
        AssertEx.NotNull(status.Upstreams[0].CircuitBreaker);
        AssertEx.Equal(1, status.Listeners.Count);
        AssertEx.Equal(ProxyListenerState.Active, status.Listeners[0].State);
        AssertEx.False(status.Upstreams is ProxyUpstreamStatus[], "Status upstreams should not expose a mutable array.");
        AssertEx.False(status.Listeners is ProxyListenerStatus[], "Status listeners should not expose a mutable array.");
        AssertEx.Throws<ArgumentNullException>(() => new ProxyStatus(listenerLive: true, listenerName: "main", endpoint: "127.0.0.1:18080", startedAt: DateTimeOffset.UnixEpoch, stoppedAt: null, lastError: null, isShuttingDown: false, shutdownStartedAtUtc: null, shutdownDeadlineUtc: null, configVersion: 7, configLoadedAtUtc: DateTimeOffset.UnixEpoch, configuredListeners: 1, configuredRoutes: 1, metrics: new ProxyMetrics().Snapshot(), upstreams: upstreams, listeners: null!, lastListenerReload: null, http3: UnknownHttp3(), routeDiagnostics: RouteDiagnosticsStatus.Enabled, configLint: ConfigLintStatus.Empty, logPersistence: ProxyLogPersistenceStatus.Unknown, readiness: ProxyReadinessStatus.Unknown, subsystems: ProxySubsystemSummaries.Unknown, runtimePreflight: ProxyRuntimePreflightStatus.Unknown));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyStatus(listenerLive: true, listenerName: "main", endpoint: "127.0.0.1:18080", startedAt: DateTimeOffset.UnixEpoch, stoppedAt: null, lastError: null, isShuttingDown: false, shutdownStartedAtUtc: null, shutdownDeadlineUtc: null, configVersion: 7, configLoadedAtUtc: DateTimeOffset.UnixEpoch, configuredListeners: 1, configuredRoutes: 1, metrics: new ProxyMetrics().Snapshot(), upstreams: [], listeners: [], lastListenerReload: null, http3: null!, routeDiagnostics: RouteDiagnosticsStatus.Enabled, configLint: ConfigLintStatus.Empty, logPersistence: ProxyLogPersistenceStatus.Unknown, readiness: ProxyReadinessStatus.Unknown, subsystems: ProxySubsystemSummaries.Unknown, runtimePreflight: ProxyRuntimePreflightStatus.Unknown));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyUpstreamStatus(RouteName: null!, UpstreamName: "primary", Endpoint: "https://primary.internal", Scheme: "https", TlsCertificateValidationEnabled: true, SniHost: "primary.internal", HealthCheckEnabled: true, HealthState: UpstreamHealthState.Healthy, LastHealthCheckResult: "status_200", LastHealthCheckAtUtc: DateTimeOffset.UnixEpoch, ConsecutiveSuccesses: 2, ConsecutiveFailures: 0, SelectedRequests: 11, RequestFailures: 0));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyUpstreamStatus(RouteName: "main", UpstreamName: null!, Endpoint: "https://primary.internal", Scheme: "https", TlsCertificateValidationEnabled: true, SniHost: "primary.internal", HealthCheckEnabled: true, HealthState: UpstreamHealthState.Healthy, LastHealthCheckResult: "status_200", LastHealthCheckAtUtc: DateTimeOffset.UnixEpoch, ConsecutiveSuccesses: 2, ConsecutiveFailures: 0, SelectedRequests: 11, RequestFailures: 0));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyUpstreamStatus(RouteName: "main", UpstreamName: "primary", Endpoint: "https://primary.internal", Scheme: "https", TlsCertificateValidationEnabled: true, SniHost: "primary.internal", HealthCheckEnabled: true, HealthState: UpstreamHealthState.Healthy, LastHealthCheckResult: "status_200", LastHealthCheckAtUtc: DateTimeOffset.UnixEpoch, ConsecutiveSuccesses: 2, ConsecutiveFailures: 0, SelectedRequests: 11, RequestFailures: 0, Protocol: null!, Weight: 1, CircuitBreaker: CircuitBreakerStatus.Disabled(CircuitBreakerPolicyInput.Disabled)));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyUpstreamStatus(RouteName: "main", UpstreamName: "primary", Endpoint: "https://primary.internal", Scheme: "https", TlsCertificateValidationEnabled: true, SniHost: "primary.internal", HealthCheckEnabled: true, HealthState: UpstreamHealthState.Healthy, LastHealthCheckResult: "status_200", LastHealthCheckAtUtc: DateTimeOffset.UnixEpoch, ConsecutiveSuccesses: 2, ConsecutiveFailures: 0, SelectedRequests: 11, RequestFailures: 0, Protocol: RuntimeUpstreamProtocol.Http1, Weight: 1, CircuitBreaker: null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyUpstreamStatusResponseMapper.FromStatuses(null!));
        var response = ProxyStatusResponseMapper.FromBusinessResponse(status);
        AssertEx.Equal("primary", response.Upstreams[0].UpstreamName);
        AssertEx.Equal(RuntimeUpstreamProtocol.Http1, response.Upstreams[0].Protocol);
        AssertEx.Equal(1, response.Upstreams[0].Weight);
        AssertEx.NotNull(response.Upstreams[0].CircuitBreaker);
        AssertEx.False(response.Upstreams is List<ProxyUpstreamStatusResponse>, "Status API upstreams should not expose a mutable list.");
        AssertEx.False(response.Upstreams is ProxyUpstreamStatusResponse[], "Status API upstreams should not expose a mutable array.");
        var apiUpstreams = new List<ProxyUpstreamStatusResponse>
        {
            response.Upstreams[0]
        };
        var apiListeners = new List<ProxyListenerStatusResponse>
        {
            response.Listeners[0]
        };
        var apiUpstreamReplacement = CreateUpstreamStatusResponse(response.Upstreams[0], upstreamName: "api-replacement");
        var directResponse = new ProxyStatusResponse(listenerLive: true, listenerName: "main", endpoint: "127.0.0.1:18080", startedAt: DateTimeOffset.UnixEpoch, stoppedAt: null, lastError: null, isShuttingDown: false, shutdownStartedAtUtc: null, shutdownDeadlineUtc: null, configVersion: 7, configLoadedAtUtc: DateTimeOffset.UnixEpoch, configuredListeners: 1, configuredRoutes: 1, metrics: response.Metrics, upstreams: apiUpstreams, listeners: apiListeners, lastListenerReload: response.LastListenerReload, http3: response.Http3, routeDiagnostics: response.RouteDiagnostics, configLint: response.ConfigLint, logPersistence: response.LogPersistence, readiness: response.Readiness, subsystems: response.Subsystems, runtimePreflight: response.RuntimePreflight);
        apiUpstreams[0] = apiUpstreamReplacement;
        apiListeners[0] = apiListeners[0] with
        {
            State = ProxyListenerStateResponse.Failed
        };
        apiUpstreams.Clear();
        apiListeners.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ProxyStatusResponse(listenerLive: true, listenerName: "main", endpoint: "127.0.0.1:18080", startedAt: DateTimeOffset.UnixEpoch, stoppedAt: null, lastError: null, isShuttingDown: false, shutdownStartedAtUtc: null, shutdownDeadlineUtc: null, configVersion: 7, configLoadedAtUtc: DateTimeOffset.UnixEpoch, configuredListeners: 1, configuredRoutes: 1, metrics: response.Metrics, upstreams: null!, listeners: [], lastListenerReload: response.LastListenerReload, http3: response.Http3, routeDiagnostics: response.RouteDiagnostics, configLint: response.ConfigLint, logPersistence: response.LogPersistence, readiness: response.Readiness, subsystems: response.Subsystems, runtimePreflight: response.RuntimePreflight));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyStatusResponse(listenerLive: true, listenerName: "main", endpoint: "127.0.0.1:18080", startedAt: DateTimeOffset.UnixEpoch, stoppedAt: null, lastError: null, isShuttingDown: false, shutdownStartedAtUtc: null, shutdownDeadlineUtc: null, configVersion: 7, configLoadedAtUtc: DateTimeOffset.UnixEpoch, configuredListeners: 1, configuredRoutes: 1, metrics: response.Metrics, upstreams: [], listeners: null!, lastListenerReload: response.LastListenerReload, http3: response.Http3, routeDiagnostics: response.RouteDiagnostics, configLint: response.ConfigLint, logPersistence: response.LogPersistence, readiness: response.Readiness, subsystems: response.Subsystems, runtimePreflight: response.RuntimePreflight));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyStatusResponse(listenerLive: true, listenerName: "main", endpoint: "127.0.0.1:18080", startedAt: DateTimeOffset.UnixEpoch, stoppedAt: null, lastError: null, isShuttingDown: false, shutdownStartedAtUtc: null, shutdownDeadlineUtc: null, configVersion: 7, configLoadedAtUtc: DateTimeOffset.UnixEpoch, configuredListeners: 1, configuredRoutes: 1, metrics: response.Metrics, upstreams: [], listeners: [], lastListenerReload: response.LastListenerReload, http3: null!, routeDiagnostics: response.RouteDiagnostics, configLint: response.ConfigLint, logPersistence: response.LogPersistence, readiness: response.Readiness, subsystems: response.Subsystems, runtimePreflight: response.RuntimePreflight));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyUpstreamStatusResponse(routeName: "main", upstreamName: "primary", endpoint: "https://primary.internal", scheme: "https", tlsCertificateValidationEnabled: true, sniHost: "primary.internal", healthCheckEnabled: true, healthState: UpstreamHealthStateResponse.Healthy, lastHealthCheckResult: "status_200", lastHealthCheckAtUtc: DateTimeOffset.UnixEpoch, consecutiveSuccesses: 2, consecutiveFailures: 0, selectedRequests: 11, requestFailures: 0, protocol: null!, weight: 1, circuitBreaker: CircuitBreakerStatusResponse.Disabled));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyUpstreamStatusResponse(routeName: "main", upstreamName: "primary", endpoint: "https://primary.internal", scheme: "https", tlsCertificateValidationEnabled: true, sniHost: "primary.internal", healthCheckEnabled: true, healthState: UpstreamHealthStateResponse.Healthy, lastHealthCheckResult: "status_200", lastHealthCheckAtUtc: DateTimeOffset.UnixEpoch, consecutiveSuccesses: 2, consecutiveFailures: 0, selectedRequests: 11, requestFailures: 0, protocol: RuntimeUpstreamProtocol.Http1, weight: 1, circuitBreaker: null!));
        AssertEx.Equal("primary", directResponse.Upstreams[0].UpstreamName);
        AssertEx.Equal(ProxyListenerStateResponse.Active, directResponse.Listeners[0].State);
        AssertEx.False(directResponse.Upstreams is ProxyUpstreamStatusResponse[], "Direct status API upstreams should not expose a mutable array.");
        AssertEx.False(directResponse.Listeners is ProxyListenerStatusResponse[], "Direct status API listeners should not expose a mutable array.");
    }

    public static void StatusInputCopiesRuntimeUpstreamAndAcmeLists()
    {
        var listener = Listener();
        var active = ListenerStatus(listener, ProxyListenerState.Active);
        var failed = ListenerStatus(listener, ProxyListenerState.Failed);
        var listenerStatuses = new List<ProxyListenerStatus>
        {
            active
        };
        var runtime = new ProxyStatusRuntimeSummary(ListenerLive: true, ListenerName: "main", Endpoint: "127.0.0.1:18080", StartedAt: DateTimeOffset.UnixEpoch, StoppedAt: null, LastError: null, IsShuttingDown: false, ShutdownStartedAtUtc: null, ShutdownDeadlineUtc: null, Listeners: listenerStatuses, LastListenerReload: null);
        var upstream = new ProxyUpstreamStatus(RouteName: "main", UpstreamName: "primary", Endpoint: "https://primary.internal", Scheme: "https", TlsCertificateValidationEnabled: true, SniHost: "primary.internal", HealthCheckEnabled: true, HealthState: UpstreamHealthState.Healthy, LastHealthCheckResult: "status_200", LastHealthCheckAtUtc: DateTimeOffset.UnixEpoch, ConsecutiveSuccesses: 2, ConsecutiveFailures: 0, SelectedRequests: 11, RequestFailures: 0);
        var replacementUpstream = CreateUpstreamStatus(upstream, upstreamName: "replacement");
        var upstreams = new List<ProxyUpstreamStatus>
        {
            upstream
        };
        var acme = new AcmeCertificateLifecycleStatus(CertificateId: "cert-a", Enabled: true, Domains: ["example.test"], Active: true, Source: "acme", NotBeforeUtc: DateTimeOffset.UnixEpoch, NotAfterUtc: DateTimeOffset.UnixEpoch.AddDays(30), RenewalDueAtUtc: DateTimeOffset.UnixEpoch.AddDays(20), LastAttemptAtUtc: null, LastSucceededAtUtc: DateTimeOffset.UnixEpoch, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: "loaded", ErrorSummary: null);
        var replacementAcme = new AcmeCertificateLifecycleStatus(CertificateId: "cert-b", Enabled: acme.Enabled, Domains: acme.Domains, Active: acme.Active, Source: acme.Source, NotBeforeUtc: acme.NotBeforeUtc, NotAfterUtc: acme.NotAfterUtc, RenewalDueAtUtc: acme.RenewalDueAtUtc, LastAttemptAtUtc: acme.LastAttemptAtUtc, LastSucceededAtUtc: acme.LastSucceededAtUtc, LastFailedAtUtc: acme.LastFailedAtUtc, NextAttemptNotBeforeUtc: acme.NextAttemptNotBeforeUtc, LastResult: acme.LastResult, ErrorSummary: acme.ErrorSummary);
        var acmeStatuses = new List<AcmeCertificateLifecycleStatus>
        {
            acme
        };
        var metrics = new ProxyMetrics().Snapshot();
        var observedAtUtc = new DateTimeOffset(2026, 6, 13, 21, 20, 0, TimeSpan.Zero);
        var http3 = UnknownHttp3();
        var readiness = ProxyStatusReadinessInputMapper.FromSources(sources: ProxyStatusReadinessSourceMapper.FromSources(ProxyStatusReadinessConfigurationSourceSet.Missing, runtime, metrics, upstreams, http3, ProxyLogPersistenceStatus.Unknown), cacheStatus: null, acmeStatuses: acmeStatuses, runtimePreflight: ProxyRuntimePreflightStatus.Unknown, observedAtUtc: observedAtUtc);
        var input = new ProxyStatusInput(Runtime: runtime, Configuration: null, Metrics: metrics, Upstreams: upstreams, Http3: http3, LogPersistence: ProxyLogPersistenceStatus.Unknown, CacheStatus: null, AcmeStatuses: acmeStatuses, RuntimePreflight: ProxyRuntimePreflightStatus.Unknown, ObservedAtUtc: observedAtUtc, Readiness: readiness, ConfigLint: ConfigLintStatus.Empty);
        AssertEx.Throws<ArgumentNullException>(() => new ProxyStatusRuntimeSummary(ListenerLive: true, ListenerName: "main", Endpoint: "127.0.0.1:18080", StartedAt: DateTimeOffset.UnixEpoch, StoppedAt: null, LastError: null, IsShuttingDown: false, ShutdownStartedAtUtc: null, ShutdownDeadlineUtc: null, Listeners: [null!], LastListenerReload: null));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyStatusInput(Runtime: runtime, Configuration: null, Metrics: metrics, Upstreams: [null!], Http3: http3, LogPersistence: ProxyLogPersistenceStatus.Unknown, CacheStatus: null, AcmeStatuses: acmeStatuses, RuntimePreflight: ProxyRuntimePreflightStatus.Unknown, ObservedAtUtc: observedAtUtc, Readiness: readiness, ConfigLint: ConfigLintStatus.Empty));
        listenerStatuses[0] = failed;
        upstreams[0] = replacementUpstream;
        acmeStatuses[0] = replacementAcme;
        listenerStatuses.Clear();
        upstreams.Clear();
        acmeStatuses.Clear();
        AssertEx.Equal(1, runtime.Listeners.Count);
        AssertEx.Equal(ProxyListenerState.Active, runtime.Listeners[0].State);
        AssertEx.Equal(1, input.Upstreams.Count);
        AssertEx.Equal("primary", input.Upstreams[0].UpstreamName);
        AssertEx.Equal(1, input.AcmeStatuses.Count);
        AssertEx.Equal("cert-a", input.AcmeStatuses[0].CertificateId);
        AssertEx.False(runtime.Listeners is ProxyListenerStatus[], "Runtime summary listeners should not expose a mutable array.");
        AssertEx.False(input.Upstreams is ProxyUpstreamStatus[], "Status input upstreams should not expose a mutable array.");
        AssertEx.False(input.AcmeStatuses is AcmeCertificateLifecycleStatus[], "Status input ACME statuses should not expose a mutable array.");
    }

    public static void StatusReadinessInputsCopySourceLists()
    {
        var configured = new ProxyConfiguredListenerSummarySource(Enabled: true, Http1Enabled: true, Http2Enabled: false, Http3EnabledForTraffic: false);
        var configuredReplacement = configured with
        {
            Enabled = false
        };
        var configuredListeners = new List<ProxyConfiguredListenerSummarySource>
        {
            configured
        };
        var runtime = new ProxyRuntimeListenerSummarySource(IsQuic: false, ProxyListenerState.Active);
        var runtimeReplacement = runtime with
        {
            State = ProxyListenerState.Failed
        };
        var runtimeListeners = new List<ProxyRuntimeListenerSummarySource>
        {
            runtime
        };
        var route = new ProxyRouteSummarySource(SiteName: "main", IsProxyRoute: true, CacheEnabled: true, HasHttp3Upstream: false);
        var routeReplacement = route with
        {
            SiteName = "replacement"
        };
        AssertEx.Throws<ArgumentException>(() => new ProxyRouteSummarySource(SiteName: " ", IsProxyRoute: true, CacheEnabled: true, HasHttp3Upstream: false));
        AssertEx.Throws<ArgumentException>(() =>
        {
            _ = route with
            {
                SiteName = " "
            };
        });
        var routes = new List<ProxyRouteSummarySource>
        {
            route
        };
        var upstream = new ProxyUpstreamSummarySource(UpstreamHealthState.Healthy, HealthCheckEnabled: true, CircuitBreakerEnabled: false, CircuitBreakerRuntimeState.Disabled);
        var upstreamReplacement = upstream with
        {
            HealthState = UpstreamHealthState.Unhealthy
        };
        var upstreams = new List<ProxyUpstreamSummarySource>
        {
            upstream
        };
        var acme = new AcmeCertificateLifecycleStatus(CertificateId: "cert-a", Enabled: true, Domains: ["example.test"], Active: true, Source: "acme", NotBeforeUtc: DateTimeOffset.UnixEpoch, NotAfterUtc: DateTimeOffset.UnixEpoch.AddDays(30), RenewalDueAtUtc: DateTimeOffset.UnixEpoch.AddDays(20), LastAttemptAtUtc: null, LastSucceededAtUtc: DateTimeOffset.UnixEpoch, LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: "loaded", ErrorSummary: null);
        var acmeReplacement = new AcmeCertificateLifecycleStatus(CertificateId: "cert-b", Enabled: acme.Enabled, Domains: acme.Domains, Active: acme.Active, Source: acme.Source, NotBeforeUtc: acme.NotBeforeUtc, NotAfterUtc: acme.NotAfterUtc, RenewalDueAtUtc: acme.RenewalDueAtUtc, LastAttemptAtUtc: acme.LastAttemptAtUtc, LastSucceededAtUtc: acme.LastSucceededAtUtc, LastFailedAtUtc: acme.LastFailedAtUtc, NextAttemptNotBeforeUtc: acme.NextAttemptNotBeforeUtc, LastResult: acme.LastResult, ErrorSummary: acme.ErrorSummary);
        var acmeStatuses = new List<AcmeCertificateLifecycleStatus>
        {
            acme
        };
        var referencedCertificates = new List<string>
        {
            "cert-a"
        };
        var loadedCertificate = new ProxyCertificateValiditySource("cert-a", DateTime.UnixEpoch, DateTime.UnixEpoch.AddDays(30));
        var loadedCertificates = new List<ProxyCertificateValiditySource>
        {
            loadedCertificate
        };
        AssertEx.Throws<ArgumentException>(() => new ProxyCertificateSummarySource([" "], loadedCertificates));
        AssertEx.Throws<ArgumentException>(() => new ProxyCertificateValiditySource(" ", DateTime.UnixEpoch, DateTime.UnixEpoch.AddDays(30)));
        AssertEx.Throws<ArgumentException>(() =>
        {
            _ = loadedCertificate with
            {
                Id = " "
            };
        });
        var certificates = new ProxyCertificateSummarySource(referencedCertificates, loadedCertificates);
        var configuration = new ProxyStatusReadinessConfigurationSourceSet(HasActiveConfiguration: true, ConfigGeneration: 42, ConfigurationLoadedAtUtc: DateTimeOffset.UnixEpoch, ConfiguredListeners: configuredListeners, Routes: routes, Certificates: certificates, Acme: new ProxyAcmeSummaryConfigurationSource(Enabled: true, ConfiguredCertificates: 1), LimitConfiguration: new ProxyLimitConfigurationSummarySource(MaxActiveClientConnections: 4096, MaxConcurrentTlsHandshakes: 16, RequestsPerMinutePerIp: 30));
        var sources = new ProxyStatusReadinessSourceSet(HasActiveConfiguration: true, ConfigGeneration: 42, ConfigurationLoadedAtUtc: DateTimeOffset.UnixEpoch, LastListenerReloadSucceeded: true, LastListenerReloadFailed: false, ConfiguredListeners: configuredListeners, RuntimeListeners: runtimeListeners, Routes: routes, Certificates: certificates, Acme: new ProxyAcmeSummaryConfigurationSource(Enabled: true, ConfiguredCertificates: 1), Upstreams: upstreams, LimitConfiguration: new ProxyLimitConfigurationSummarySource(MaxActiveClientConnections: 4096, MaxConcurrentTlsHandshakes: 16, RequestsPerMinutePerIp: 30), LimitRuntime: new ProxyLimitRuntimeSummarySource(ActiveConnections: 1, ActiveTlsHandshakes: 0, ActiveHttp2Streams: 0, ActiveHttp3Streams: 0, ActiveUpstreamHttp3Streams: 0), ClientHttp3Enabled: false, ClientHttp3Ready: false, Log: new ProxyLogSummarySource(AccessLogPersistenceEnabled: true, AdminAuditPersistenceEnabled: true, State: ProxyStatusText.Healthy, Reason: "ok"), Shutdown: new ProxyShutdownSummarySource(IsRunning: true, IsShuttingDown: false, ShutdownStartedAtUtc: null, ShutdownDeadlineUtc: null));
        var input = new ProxyStatusReadinessInput(HasActiveConfiguration: true, ConfigGeneration: 42, ConfigurationLoadedAtUtc: DateTimeOffset.UnixEpoch, LastListenerReloadSucceeded: true, LastListenerReloadFailed: false, ConfiguredListeners: configuredListeners, RuntimeListeners: runtimeListeners, Routes: routes, Certificates: certificates, Acme: new ProxyAcmeSummaryConfigurationSource(Enabled: true, ConfiguredCertificates: 1), Upstreams: upstreams, LimitConfiguration: new ProxyLimitConfigurationSummarySource(MaxActiveClientConnections: 4096, MaxConcurrentTlsHandshakes: 16, RequestsPerMinutePerIp: 30), LimitRuntime: new ProxyLimitRuntimeSummarySource(ActiveConnections: 1, ActiveTlsHandshakes: 0, ActiveHttp2Streams: 0, ActiveHttp3Streams: 0, ActiveUpstreamHttp3Streams: 0), ClientHttp3Enabled: false, ClientHttp3Ready: false, Log: new ProxyLogSummarySource(AccessLogPersistenceEnabled: true, AdminAuditPersistenceEnabled: true, State: ProxyStatusText.Healthy, Reason: "ok"), Shutdown: new ProxyShutdownSummarySource(IsRunning: true, IsShuttingDown: false, ShutdownStartedAtUtc: null, ShutdownDeadlineUtc: null), CacheStatus: null, AcmeStatuses: acmeStatuses, RuntimePreflight: ProxyRuntimePreflightStatus.Unknown, ObservedAtUtc: DateTimeOffset.UnixEpoch);
        AssertEx.Throws<ArgumentException>(() => new ProxyLogSummarySource(AccessLogPersistenceEnabled: true, AdminAuditPersistenceEnabled: true, State: " ", Reason: "ok"));
        AssertEx.Throws<ArgumentException>(() => new ProxyLogSummarySource(AccessLogPersistenceEnabled: true, AdminAuditPersistenceEnabled: true, State: ProxyStatusText.Healthy, Reason: " "));
        AssertEx.Throws<ArgumentException>(() => new ProxyLogPersistenceFailureStatus(TimestampUtc: DateTimeOffset.UnixEpoch, Category: " ", Reason: "write_failed"));
        AssertEx.Throws<ArgumentException>(() => new ProxyLogPersistenceFailureStatus(TimestampUtc: DateTimeOffset.UnixEpoch, Category: "io", Reason: " "));
        configuredListeners[0] = configuredReplacement;
        runtimeListeners[0] = runtimeReplacement;
        routes[0] = routeReplacement;
        upstreams[0] = upstreamReplacement;
        acmeStatuses[0] = acmeReplacement;
        referencedCertificates[0] = "cert-b";
        loadedCertificates[0] = loadedCertificate with
        {
            Id = "cert-b"
        };
        configuredListeners.Clear();
        runtimeListeners.Clear();
        routes.Clear();
        upstreams.Clear();
        acmeStatuses.Clear();
        referencedCertificates.Clear();
        loadedCertificates.Clear();
        AssertEx.True(configuration.ConfiguredListeners[0].Enabled);
        AssertEx.Equal("main", configuration.Routes[0].SiteName);
        AssertEx.True(sources.ConfiguredListeners[0].Enabled);
        AssertEx.Equal(ProxyListenerState.Active, sources.RuntimeListeners[0].State);
        AssertEx.Equal("main", sources.Routes[0].SiteName);
        AssertEx.Equal(UpstreamHealthState.Healthy, sources.Upstreams[0].HealthState);
        AssertEx.True(input.ConfiguredListeners[0].Enabled);
        AssertEx.Equal(ProxyListenerState.Active, input.RuntimeListeners[0].State);
        AssertEx.Equal("main", input.Routes[0].SiteName);
        AssertEx.Equal(UpstreamHealthState.Healthy, input.Upstreams[0].HealthState);
        AssertEx.Equal("cert-a", input.AcmeStatuses[0].CertificateId);
        AssertEx.Equal("cert-a", certificates.ReferencedCertificateIds[0]);
        AssertEx.Equal("cert-a", certificates.LoadedCertificates[0].Id);
        AssertEx.False(input.Routes is ProxyRouteSummarySource[], "Readiness input routes should not expose a mutable array.");
        AssertEx.False(sources.Upstreams is ProxyUpstreamSummarySource[], "Readiness source upstreams should not expose a mutable array.");
        AssertEx.False(certificates.ReferencedCertificateIds is string[], "Certificate references should not expose a mutable array.");
        var readiness = ProxyReadinessStatus.Evaluated(ProxyStatusText.Degraded, ["runtime_preflight_degraded"], DateTimeOffset.UnixEpoch, configGeneration: 42);
        AssertEx.False(readiness.Reasons is string[], "Readiness status reasons should not expose a mutable array.");
        var readinessResponse = ProxyReadinessStatusResponseMapper.FromStatus(readiness);
        AssertEx.Equal("runtime_preflight_degraded", readinessResponse.Reasons[0]);
        AssertEx.False(ReferenceEquals(readiness.Reasons, readinessResponse.Reasons), "Readiness API reasons should not reuse the BLL reasons collection.");
        AssertEx.False(readinessResponse.Reasons is string[], "Readiness API reasons should not expose a mutable array.");
        var responseReasons = new List<string>
        {
            readinessResponse.Reasons[0]
        };
        var directReadinessResponse = new ProxyReadinessStatusResponse(state: ProxyStatusText.Degraded, reasons: responseReasons, generatedAtUtc: DateTimeOffset.UnixEpoch, configGeneration: 42);
        responseReasons[0] = "replacement_reason";
        responseReasons.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ProxyReadinessStatusResponse(state: ProxyStatusText.Degraded, reasons: null!, generatedAtUtc: DateTimeOffset.UnixEpoch, configGeneration: 42));
        AssertEx.Equal("runtime_preflight_degraded", directReadinessResponse.Reasons[0]);
        AssertEx.False(directReadinessResponse.Reasons is string[], "Direct readiness API reasons should not expose a mutable array.");
    }

    public static void StatusReadinessSourceMapperConsumesRuntimeSummaryWithoutRuntimeSnapshot()
    {
        var listener = Listener();
        var reload = ProxyListenerReloadResult.Failed(DateTimeOffset.UnixEpoch, added: 0, removed: 0, changed: 1, unchanged: 0, changes: [], errors: ["bind_failed"]);
        var runtime = new ProxyStatusRuntimeSummary(ListenerLive: false, ListenerName: null, Endpoint: null, StartedAt: null, StoppedAt: DateTimeOffset.UnixEpoch, LastError: "bind_failed", IsShuttingDown: true, ShutdownStartedAtUtc: DateTimeOffset.UnixEpoch.AddMinutes(1), ShutdownDeadlineUtc: DateTimeOffset.UnixEpoch.AddMinutes(2), Listeners: [ListenerStatus(listener, ProxyListenerState.Failed)], LastListenerReload: reload);
        var metrics = new ProxyMetrics().Snapshot();
        var http3 = new RuntimeHttp3SupportProjection("unknown", QuicListenerSupported: false, QuicConnectionSupported: false, "disabled", "disabled", EnabledForTraffic: false, QuicListenerReady: false, AltSvcConfigured: false, AltSvcActive: false, AltSvcMaxAgeSeconds: null, "not_configured", UdpQuicListenerIdentityModeled: true, "client_http3_default_enabled_for_eligible_tls_proxy_listeners");
        var logPersistence = ProxyLogPersistenceStatus.Unknown;
        AssertEx.Throws<ArgumentNullException>(() => ProxyUpstreamSummarySourceMapper.FromStatusResponses(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyLogSummarySourceMapper.FromStatus(null!));
        var sources = ProxyStatusReadinessSourceMapper.FromSources(ProxyStatusReadinessConfigurationSourceSet.Missing, runtime, metrics, upstreams: [], http3, logPersistence);
        AssertEx.False(sources.HasActiveConfiguration);
        AssertEx.False(sources.LastListenerReloadSucceeded!.Value);
        AssertEx.True(sources.LastListenerReloadFailed);
        AssertEx.Equal(1, sources.RuntimeListeners.Count);
        AssertEx.Equal(ProxyListenerState.Failed, sources.RuntimeListeners[0].State);
        AssertEx.False(sources.Shutdown.IsRunning);
        AssertEx.True(sources.Shutdown.IsShuttingDown);
        AssertEx.Equal(DateTimeOffset.UnixEpoch.AddMinutes(1), sources.Shutdown.ShutdownStartedAtUtc);
    }

    public static void StatusReadinessConfigurationSourceSetNamesMissingConfiguration()
    {
        var configuration = ProxyStatusReadinessConfigurationSourceSet.Missing;
        AssertEx.False(configuration.HasActiveConfiguration);
        AssertEx.Equal<int?>(null, configuration.ConfigGeneration);
        AssertEx.Equal<DateTimeOffset?>(null, configuration.ConfigurationLoadedAtUtc);
        AssertEx.Equal(0, configuration.ConfiguredListeners.Count);
        AssertEx.Equal(0, configuration.Routes.Count);
        AssertEx.Equal(null, configuration.Certificates);
        AssertEx.Equal(null, configuration.Acme);
        AssertEx.Equal(null, configuration.LimitConfiguration);
    }

    public static void StatusReadinessConfigurationSourceMapperReadsEnumerableFactsWithoutSnapshot()
    {
        using var certificate = X509CertificateLoader.LoadPkcs12(TestCertificates.CreateSelfSignedPfxBytes("readiness-cert.example.test"), null);
        var listener = Listener(defaultCertificateId: "default", protocols: RuntimeListenerProtocols.Http1AndHttp2).WithSniCertificates([new RuntimeSniCertificateBinding("alt.example.test", "alt")]);
        var route = StaticRoute(cache: CachePolicy());
        var runtimeCertificate = new RuntimeCertificate("default", "default.pfx", "pfx", false, certificate, "manualPfx", ["readiness-cert.example.test"]);
        var listenerSources = new List<RuntimeListener>
        {
            listener
        };
        var routeSources = new List<RuntimeRoute>
        {
            route
        };
        var certificateSources = new List<RuntimeCertificate>
        {
            runtimeCertificate
        };
        var source = ProxyStatusReadinessConfigurationSourceMapper.FromSources(version: 24, DateTimeOffset.UnixEpoch.AddMinutes(24), listenerSources.Select(static source => source), routeSources.Select(static source => source), certificateSources.Select(static source => source), new RuntimeAcmeOptions(false, true, "", [], false, "acme", 30, 720, 60, []), new RuntimeLimits(4096, 128, 240, 30, 32768, 128, 8192, 104857600, 8192, TimeSpan.FromSeconds(15)));
        listenerSources.Clear();
        routeSources.Clear();
        certificateSources.Clear();
        AssertEx.True(source.HasActiveConfiguration);
        AssertEx.Equal(24, source.ConfigGeneration!.Value);
        AssertEx.Equal(DateTimeOffset.UnixEpoch.AddMinutes(24), source.ConfigurationLoadedAtUtc!.Value);
        AssertEx.Equal(1, source.ConfiguredListeners.Count);
        AssertEx.True(source.ConfiguredListeners[0].Http1Enabled);
        AssertEx.True(source.ConfiguredListeners[0].Http2Enabled);
        AssertEx.Equal(1, source.Routes.Count);
        AssertEx.Equal("main", source.Routes[0].SiteName);
        var certificates = AssertEx.NotNull(source.Certificates);
        AssertEx.Equal("default", certificates.ReferencedCertificateIds[0]);
        AssertEx.Equal("alt", certificates.ReferencedCertificateIds[1]);
        AssertEx.Equal("default", certificates.LoadedCertificates[0].Id);
        AssertEx.False(source.ConfiguredListeners is ProxyConfiguredListenerSummarySource[], "Readiness configured listener sources should not expose a mutable array.");
        AssertEx.False(source.Routes is ProxyRouteSummarySource[], "Readiness route sources should not expose a mutable array.");
    }

    public static void StatusReadinessSourceMapperConsumesConfigurationSourceSetWithoutConfigurationSnapshot()
    {
        var configuration = new ProxyStatusReadinessConfigurationSourceSet(true, 42, DateTimeOffset.UnixEpoch.AddHours(1), [new ProxyConfiguredListenerSummarySource(true, true, false, false)], [new ProxyRouteSummarySource("site-a", true, true, false)], new ProxyCertificateSummarySource(["cert-a"], []), new ProxyAcmeSummaryConfigurationSource(true, 1), new ProxyLimitConfigurationSummarySource(100, 4, 600));
        var runtime = new ProxyStatusRuntimeSummary(ListenerLive: true, ListenerName: "main", Endpoint: "127.0.0.1:18080", StartedAt: DateTimeOffset.UnixEpoch, StoppedAt: null, LastError: null, IsShuttingDown: false, ShutdownStartedAtUtc: null, ShutdownDeadlineUtc: null, Listeners: [], LastListenerReload: null);
        var http3 = new RuntimeHttp3SupportProjection("unknown", QuicListenerSupported: false, QuicConnectionSupported: false, "disabled", "disabled", EnabledForTraffic: false, QuicListenerReady: false, AltSvcConfigured: false, AltSvcActive: false, AltSvcMaxAgeSeconds: null, "not_configured", UdpQuicListenerIdentityModeled: true, "client_http3_default_enabled_for_eligible_tls_proxy_listeners");
        var sources = ProxyStatusReadinessSourceMapper.FromSources(configuration, runtime, new ProxyMetrics().Snapshot(), upstreams: [], http3, ProxyLogPersistenceStatus.Unknown);
        AssertEx.True(sources.HasActiveConfiguration);
        AssertEx.Equal(42, sources.ConfigGeneration!.Value);
        AssertEx.Equal(DateTimeOffset.UnixEpoch.AddHours(1), sources.ConfigurationLoadedAtUtc!.Value);
        AssertEx.Equal(1, sources.ConfiguredListeners.Count);
        AssertEx.Equal(1, sources.Routes.Count);
        AssertEx.Equal("site-a", sources.Routes[0].SiteName);
        AssertEx.Equal("cert-a", AssertEx.NotNull(sources.Certificates).ReferencedCertificateIds[0]);
        AssertEx.True(AssertEx.NotNull(sources.Acme).Enabled);
        AssertEx.Equal(600, AssertEx.NotNull(sources.LimitConfiguration).RequestsPerMinutePerIp);
    }

    public static void StatusListenerAndRouteSourceMappersReadCollectionsWithoutConfigurationSnapshot()
    {
        var listener = Listener(RuntimeListenerTransport.Https, "status-cert", RuntimeListenerProtocols.Http1AndHttp3);
        var upstream = Upstream(scheme: "https", protocol: RuntimeUpstreamProtocol.Http3);
        var route = BaseRoute(RuntimeRouteAction.Proxy, [upstream], CachePolicy());
        RuntimeListener[] listenerSources = [listener];
        RuntimeRoute[] routeSources = [route];
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfiguredListenerSummarySourceMapper.FromListeners(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteSummarySourceMapper.FromRoutes(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfiguredListenerSummarySourceMapper.FromListeners([null!]));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteSummarySourceMapper.FromRoutes([null!]));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteSummarySourceMapper.FromRoutes([route.WithUpstreams([null!])]));
        var listeners = ProxyConfiguredListenerSummarySourceMapper.FromListeners(listenerSources.Select(static source => source));
        var routes = ProxyRouteSummarySourceMapper.FromRoutes(routeSources.Select(static source => source));
        AssertEx.Equal(1, listeners.Count);
        AssertEx.True(listeners[0].Enabled);
        AssertEx.True(listeners[0].Http1Enabled);
        AssertEx.False(listeners[0].Http2Enabled);
        AssertEx.True(listeners[0].Http3EnabledForTraffic);
        AssertEx.False(listeners is ProxyConfiguredListenerSummarySource[], "Status configured listener sources should not expose a mutable array.");
        AssertEx.Equal(1, routes.Count);
        AssertEx.Equal("main", routes[0].SiteName);
        AssertEx.True(routes[0].IsProxyRoute);
        AssertEx.True(routes[0].CacheEnabled);
        AssertEx.True(routes[0].HasHttp3Upstream);
        AssertEx.False(routes is ProxyRouteSummarySource[], "Status route summary sources should not expose a mutable array.");
    }

    public static void StatusAcmeSummaryConfigurationMapperReadsAcmeOptionsWithoutConfigurationSnapshot()
    {
        var acme = new RuntimeAcmeOptions(Enabled: true, UseStaging: false, DirectoryUrl: "https://acme.example/directory", ContactEmails: ["ops@example.test"], TermsAccepted: true, StoragePath: "acme", RenewBeforeDays: 30, CheckIntervalMinutes: 720, RetryAfterMinutes: 60, Certificates: [new RuntimeAcmeCertificateOptions("first", true, ["first.example.test"], 30), new RuntimeAcmeCertificateOptions("second", false, ["second.example.test"], 30)]);
        var source = ProxyAcmeSummaryConfigurationSourceMapper.FromSource(acme);
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyAcmeSummaryConfigurationSource(Enabled: true, ConfiguredCertificates: -1));
        AssertEx.True(source.Enabled);
        AssertEx.Equal(1, source.ConfiguredCertificates);
    }

    public static void StatusCertificateSummarySourceMapperReadsListenersAndCertificatesWithoutSnapshot()
    {
        using var certificate = X509CertificateLoader.LoadPkcs12(TestCertificates.CreateSelfSignedPfxBytes("status-cert.example.test"), null);
        var runtimeCertificate = new RuntimeCertificate("default", "default.pfx", "pfx", false, certificate, "manualPfx", ["status-cert.example.test"]);
        var listener = Listener(defaultCertificateId: "default").WithSniCertificates([new RuntimeSniCertificateBinding("alt.example.test", "alt")]);
        var listenerSources = new List<RuntimeListener>
        {
            listener
        };
        var certificateSources = new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = runtimeCertificate
        };
        AssertEx.Throws<ArgumentNullException>(() => ProxyCertificateSummarySourceMapper.FromSources(null!, certificateSources.Values));
        AssertEx.Throws<ArgumentNullException>(() => ProxyCertificateSummarySourceMapper.FromSources(listenerSources, null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyCertificateSummarySourceMapper.FromSources([null!], certificateSources.Values));
        AssertEx.Throws<ArgumentNullException>(() => ProxyCertificateSummarySourceMapper.FromSources([listener.WithSniCertificates([null!])], certificateSources.Values));
        AssertEx.Throws<ArgumentNullException>(() => ProxyCertificateSummarySourceMapper.FromSources(listenerSources, [null!]));
        var source = ProxyCertificateSummarySourceMapper.FromSources(listenerSources.Select(static source => source), certificateSources.Values.Select(static source => source));
        listenerSources.Clear();
        certificateSources.Clear();
        AssertEx.Equal(2, source.ReferencedCertificateIds.Count);
        AssertEx.Equal("default", source.ReferencedCertificateIds[0]);
        AssertEx.Equal("alt", source.ReferencedCertificateIds[1]);
        AssertEx.Equal(1, source.LoadedCertificates.Count);
        AssertEx.Equal("default", source.LoadedCertificates[0].Id);
        AssertEx.Equal(certificate.NotBefore, source.LoadedCertificates[0].NotBefore);
        AssertEx.Equal(certificate.NotAfter, source.LoadedCertificates[0].NotAfter);
        AssertEx.False(source.ReferencedCertificateIds is string[], "Status certificate references should not expose a mutable array.");
        AssertEx.False(source.LoadedCertificates is ProxyCertificateValiditySource[], "Status loaded certificate sources should not expose a mutable array.");
    }

    public static void StatusLimitSummaryConfigurationMapperReadsLimitsWithoutConfigurationSnapshot()
    {
        var limits = new RuntimeLimits(MaxActiveClientConnections: 123, MaxConcurrentTlsHandshakes: 7, RequestsPerMinutePerIp: 45, UpgradeRequestsPerMinutePerIp: 9, MaxRequestHeadBytes: 32768, MaxHeaderCount: 128, MaxHeaderLineBytes: 8192, MaxRequestBodyBytes: 104857600, MaxPathBytes: 8192, ShutdownGracePeriod: TimeSpan.FromSeconds(15));
        var source = ProxyLimitConfigurationSummarySourceMapper.FromSource(limits);
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyLimitConfigurationSummarySource(MaxActiveClientConnections: -1, MaxConcurrentTlsHandshakes: 7, RequestsPerMinutePerIp: 45));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyLimitConfigurationSummarySource(MaxActiveClientConnections: 123, MaxConcurrentTlsHandshakes: -1, RequestsPerMinutePerIp: 45));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyLimitConfigurationSummarySource(MaxActiveClientConnections: 123, MaxConcurrentTlsHandshakes: 7, RequestsPerMinutePerIp: -1));
        AssertEx.Equal(123, source.MaxActiveClientConnections);
        AssertEx.Equal(7, source.MaxConcurrentTlsHandshakes);
        AssertEx.Equal(45, source.RequestsPerMinutePerIp);
    }

    public static void ReadinessEvaluatorConsumesNarrowFactsWithoutRuntimeSnapshots()
    {
        var evaluatedAt = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
        var input = new ProxyReadinessEvaluationInput(HasActiveConfiguration: true, ConfigGeneration: 77, IsShuttingDown: false, LastListenerReloadFailed: true, LogPersistenceState: ProxyStatusText.Healthy, RuntimePreflight: ProxyRuntimePreflightStatus.Unknown, Subsystems: HealthyReadinessSubsystems()with { Listeners = new ProxyListenerSubsystemSummary(1, 1, 1, 1, 0, 1, 0, 0, 0) }, EvaluatedAtUtc: evaluatedAt);
        var readiness = ProxyReadinessEvaluator.Evaluate(input);
        AssertEx.Equal("degraded", readiness.State);
        AssertEx.True(readiness.Reasons.Contains("listener_start_failed"), string.Join(",", readiness.Reasons));
        AssertEx.True(readiness.Reasons.Contains("last_listener_reload_failed"), string.Join(",", readiness.Reasons));
        AssertEx.Equal(evaluatedAt, readiness.GeneratedAtUtc);
        AssertEx.Equal(77, readiness.ConfigGeneration);
    }

    public static void StatusReadinessBuilderConsumesNamedFactsWithoutRuntimeSnapshots()
    {
        var observedAt = new DateTimeOffset(2026, 6, 10, 8, 0, 0, TimeSpan.Zero);
        var input = new ProxyStatusReadinessInput(HasActiveConfiguration: true, ConfigGeneration: 88, ConfigurationLoadedAtUtc: observedAt.AddMinutes(-5), LastListenerReloadSucceeded: true, LastListenerReloadFailed: false, ConfiguredListeners: [new ProxyConfiguredListenerSummarySource(Enabled: true, Http1Enabled: true, Http2Enabled: false, Http3EnabledForTraffic: false)], RuntimeListeners: [new ProxyRuntimeListenerSummarySource(IsQuic: false, ProxyListenerState.Active)], Routes: [new ProxyRouteSummarySource(SiteName: "main", IsProxyRoute: true, CacheEnabled: true, HasHttp3Upstream: false)], Certificates: null, Acme: new ProxyAcmeSummaryConfigurationSource(Enabled: false, ConfiguredCertificates: 0), Upstreams: [new ProxyUpstreamSummarySource(UpstreamHealthState.Healthy, HealthCheckEnabled: true, CircuitBreakerEnabled: false, CircuitBreakerRuntimeState.Disabled)], LimitConfiguration: new ProxyLimitConfigurationSummarySource(MaxActiveClientConnections: 4096, MaxConcurrentTlsHandshakes: 16, RequestsPerMinutePerIp: 30), LimitRuntime: new ProxyLimitRuntimeSummarySource(ActiveConnections: 1, ActiveTlsHandshakes: 0, ActiveHttp2Streams: 0, ActiveHttp3Streams: 0, ActiveUpstreamHttp3Streams: 0), ClientHttp3Enabled: false, ClientHttp3Ready: false, Log: new ProxyLogSummarySource(AccessLogPersistenceEnabled: true, AdminAuditPersistenceEnabled: true, State: ProxyStatusText.Healthy, Reason: "ok"), Shutdown: new ProxyShutdownSummarySource(IsRunning: true, IsShuttingDown: false, ShutdownStartedAtUtc: null, ShutdownDeadlineUtc: null), CacheStatus: ProxyCacheStatus.FromSources(entryCount: 1, approximateBytes: 512, hitCount: 0, missCount: 0, storeCount: 0, evictionCount: 0, storeRejectionCount: 0, lastClearedAtUtc: null, lastClearReason: null, rejections: [], routes: []), AcmeStatuses: [], RuntimePreflight: ProxyRuntimePreflightStatus.Unknown, ObservedAtUtc: observedAt);
        var(readiness, subsystems) = ProxyStatusReadinessBuilder.Build(input);
        AssertEx.Equal("healthy", readiness.State);
        AssertEx.Equal(88, readiness.ConfigGeneration);
        AssertEx.Equal(observedAt, readiness.GeneratedAtUtc);
        AssertEx.Equal(1, subsystems.Listeners.Active);
        AssertEx.Equal(1, subsystems.Routes.CacheEnabledRoutes);
        AssertEx.Equal(1, subsystems.Cache.EntryCount);
        AssertEx.Equal(1, subsystems.Limits.ActiveConnections);
    }

    public static void StatusCacheAndLimitSubsystemSummariesRejectNegativeOutputFacts()
    {
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyCacheSubsystemSummary(Enabled: true, EnabledRoutes: -1, EntryCount: 1, ApproximateBytes: 512));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyCacheSubsystemSummary(Enabled: true, EnabledRoutes: 1, EntryCount: -1, ApproximateBytes: 512));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyCacheSubsystemSummary(Enabled: true, EnabledRoutes: 1, EntryCount: 1, ApproximateBytes: -1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyLimitSubsystemSummary(MaxActiveClientConnections: -1, ActiveConnections: 0, MaxConcurrentTlsHandshakes: 16, ActiveTlsHandshakes: 0, ActiveHttp2Streams: 0, ActiveHttp3Streams: 0, ActiveUpstreamHttp3Streams: 0, RequestsPerMinutePerIp: 30));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyLimitSubsystemSummary(MaxActiveClientConnections: 4096, ActiveConnections: -1, MaxConcurrentTlsHandshakes: 16, ActiveTlsHandshakes: 0, ActiveHttp2Streams: 0, ActiveHttp3Streams: 0, ActiveUpstreamHttp3Streams: 0, RequestsPerMinutePerIp: 30));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyLimitSubsystemSummary(MaxActiveClientConnections: 4096, ActiveConnections: 0, MaxConcurrentTlsHandshakes: -1, ActiveTlsHandshakes: 0, ActiveHttp2Streams: 0, ActiveHttp3Streams: 0, ActiveUpstreamHttp3Streams: 0, RequestsPerMinutePerIp: 30));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyLimitSubsystemSummary(MaxActiveClientConnections: 4096, ActiveConnections: 0, MaxConcurrentTlsHandshakes: 16, ActiveTlsHandshakes: -1, ActiveHttp2Streams: 0, ActiveHttp3Streams: 0, ActiveUpstreamHttp3Streams: 0, RequestsPerMinutePerIp: 30));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyLimitSubsystemSummary(MaxActiveClientConnections: 4096, ActiveConnections: 0, MaxConcurrentTlsHandshakes: 16, ActiveTlsHandshakes: 0, ActiveHttp2Streams: -1, ActiveHttp3Streams: 0, ActiveUpstreamHttp3Streams: 0, RequestsPerMinutePerIp: 30));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyLimitSubsystemSummary(MaxActiveClientConnections: 4096, ActiveConnections: 0, MaxConcurrentTlsHandshakes: 16, ActiveTlsHandshakes: 0, ActiveHttp2Streams: 0, ActiveHttp3Streams: -1, ActiveUpstreamHttp3Streams: 0, RequestsPerMinutePerIp: 30));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyLimitSubsystemSummary(MaxActiveClientConnections: 4096, ActiveConnections: 0, MaxConcurrentTlsHandshakes: 16, ActiveTlsHandshakes: 0, ActiveHttp2Streams: 0, ActiveHttp3Streams: 0, ActiveUpstreamHttp3Streams: -1, RequestsPerMinutePerIp: 30));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyLimitSubsystemSummary(MaxActiveClientConnections: 4096, ActiveConnections: 0, MaxConcurrentTlsHandshakes: 16, ActiveTlsHandshakes: 0, ActiveHttp2Streams: 0, ActiveHttp3Streams: 0, ActiveUpstreamHttp3Streams: 0, RequestsPerMinutePerIp: -1));
    }

    public static void StatusCountSubsystemSummariesRejectNegativeOutputFacts()
    {
        AssertListenerSummaryRejects(Configured: -1);
        AssertListenerSummaryRejects(Enabled: -1);
        AssertListenerSummaryRejects(Active: -1);
        AssertListenerSummaryRejects(Failed: -1);
        AssertListenerSummaryRejects(Draining: -1);
        AssertListenerSummaryRejects(Http1Enabled: -1);
        AssertListenerSummaryRejects(Http2Enabled: -1);
        AssertListenerSummaryRejects(Http3Enabled: -1);
        AssertListenerSummaryRejects(QuicReady: -1);
        AssertRouteSummaryRejects(Sites: -1);
        AssertRouteSummaryRejects(Routes: -1);
        AssertRouteSummaryRejects(ProxyRoutes: -1);
        AssertRouteSummaryRejects(GeneratedRoutes: -1);
        AssertRouteSummaryRejects(CacheEnabledRoutes: -1);
        AssertUpstreamSummaryRejects(Total: -1);
        AssertUpstreamSummaryRejects(Healthy: -1);
        AssertUpstreamSummaryRejects(Unhealthy: -1);
        AssertUpstreamSummaryRejects(UnknownHealth: -1);
        AssertUpstreamSummaryRejects(HealthChecksEnabled: -1);
        AssertCertificateSummaryRejects(Configured: -1);
        AssertCertificateSummaryRejects(Loaded: -1);
        AssertCertificateSummaryRejects(MissingReferences: -1);
        AssertCertificateSummaryRejects(Expired: -1);
        AssertCertificateSummaryRejects(NotYetValid: -1);
        AssertCertificateSummaryRejects(ExpiringSoon: -1);
        AssertAcmeSummaryRejects(Configured: -1);
        AssertAcmeSummaryRejects(Active: -1);
        AssertAcmeSummaryRejects(Failed: -1);
        AssertAcmeSummaryRejects(RenewalBackoff: -1);
        AssertCircuitSummaryRejects(Enabled: -1);
        AssertCircuitSummaryRejects(Open: -1);
        AssertCircuitSummaryRejects(HalfOpen: -1);
        AssertCircuitSummaryRejects(Closed: -1);
        static void AssertListenerSummaryRejects(int Configured = 1, int Enabled = 1, int Active = 1, int Failed = 0, int Draining = 0, int Http1Enabled = 1, int Http2Enabled = 0, int Http3Enabled = 0, int QuicReady = 0)
        {
            AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyListenerSubsystemSummary(Configured, Enabled, Active, Failed, Draining, Http1Enabled, Http2Enabled, Http3Enabled, QuicReady));
        }

        static void AssertRouteSummaryRejects(int Sites = 1, int Routes = 1, int ProxyRoutes = 1, int GeneratedRoutes = 0, int CacheEnabledRoutes = 0)
        {
            AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyRouteSubsystemSummary(Sites, Routes, ProxyRoutes, GeneratedRoutes, CacheEnabledRoutes));
        }

        static void AssertUpstreamSummaryRejects(int Total = 1, int Healthy = 1, int Unhealthy = 0, int UnknownHealth = 0, int HealthChecksEnabled = 0)
        {
            AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyUpstreamSubsystemSummary(Total, Healthy, Unhealthy, UnknownHealth, HealthChecksEnabled));
        }

        static void AssertCertificateSummaryRejects(int Configured = 1, int Loaded = 1, int MissingReferences = 0, int Expired = 0, int NotYetValid = 0, int ExpiringSoon = 0)
        {
            AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyCertificateSubsystemSummary(Configured, Loaded, MissingReferences, Expired, NotYetValid, ExpiringSoon, LastIssue: null));
        }

        static void AssertAcmeSummaryRejects(int Configured = 1, int Active = 1, int Failed = 0, int RenewalBackoff = 0)
        {
            AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyAcmeSubsystemSummary(Enabled: true, Configured, Active, Failed, RenewalBackoff, LastIssue: null));
        }

        static void AssertCircuitSummaryRejects(int Enabled = 1, int Open = 0, int HalfOpen = 0, int Closed = 1)
        {
            AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyCircuitSubsystemSummary(Enabled, Open, HalfOpen, Closed));
        }
    }

    public static void StatusTextSubsystemSummariesRejectBlankOutputFacts()
    {
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigSubsystemSummary(Active: true, Generation: 1, LoadedAtUtc: DateTimeOffset.UnixEpoch, LastListenerReloadSucceeded: true, LastListenerReloadReason: " "));
        AssertEx.Throws<ArgumentException>(() => new ProxySubsystemIssueSummary(TimestampUtc: DateTimeOffset.UnixEpoch, Category: " ", Reason: "missing_reference", AffectedIdentity: "cert-a"));
        AssertEx.Throws<ArgumentException>(() => new ProxySubsystemIssueSummary(TimestampUtc: DateTimeOffset.UnixEpoch, Category: "certificate", Reason: " ", AffectedIdentity: "cert-a"));
        AssertEx.Throws<ArgumentException>(() => new ProxySubsystemIssueSummary(TimestampUtc: DateTimeOffset.UnixEpoch, Category: "certificate", Reason: "missing_reference", AffectedIdentity: " "));
        AssertEx.Throws<ArgumentException>(() => new ProxyLogSubsystemSummary(AccessLogPersistenceEnabled: true, AdminAuditPersistenceEnabled: true, State: " ", Reason: ProxyStatusText.Ready));
        AssertEx.Throws<ArgumentException>(() => new ProxyLogSubsystemSummary(AccessLogPersistenceEnabled: true, AdminAuditPersistenceEnabled: true, State: ProxyStatusText.Healthy, Reason: " "));
        AssertEx.Throws<ArgumentException>(() => new ProxyProtocolSubsystemSummary(clientHttp1Enabled: true, clientHttp2Enabled: false, clientHttp3Enabled: false, clientHttp3Ready: false, upstreamHttp3Configured: false, unsupportedHttp3Features: [" "]));
        var config = new ProxyConfigSubsystemSummary(Active: false, Generation: null, LoadedAtUtc: null, LastListenerReloadSucceeded: null, LastListenerReloadReason: null);
        var issue = new ProxySubsystemIssueSummary(TimestampUtc: DateTimeOffset.UnixEpoch, Category: "certificate", Reason: "missing_reference", AffectedIdentity: null);
        AssertEx.Equal(null, config.LastListenerReloadReason);
        AssertEx.Equal(null, issue.AffectedIdentity);
    }

    public static void StatusShutdownSummariesRejectIncoherentLifecycleFacts()
    {
        var startedAt = DateTimeOffset.UnixEpoch.AddMinutes(1);
        var deadline = startedAt.AddMinutes(2);
        AssertEx.Throws<ArgumentException>(() => new ProxyShutdownSummarySource(IsRunning: true, IsShuttingDown: false, ShutdownStartedAtUtc: startedAt, ShutdownDeadlineUtc: null));
        AssertEx.Throws<ArgumentException>(() => new ProxyShutdownSummarySource(IsRunning: true, IsShuttingDown: false, ShutdownStartedAtUtc: null, ShutdownDeadlineUtc: deadline));
        AssertEx.Throws<ArgumentException>(() => new ProxyShutdownSummarySource(IsRunning: false, IsShuttingDown: true, ShutdownStartedAtUtc: null, ShutdownDeadlineUtc: deadline));
        AssertEx.Throws<ArgumentException>(() => new ProxyShutdownSummarySource(IsRunning: false, IsShuttingDown: true, ShutdownStartedAtUtc: startedAt, ShutdownDeadlineUtc: null));
        AssertEx.Throws<ArgumentException>(() => new ProxyShutdownSummarySource(IsRunning: false, IsShuttingDown: true, ShutdownStartedAtUtc: startedAt, ShutdownDeadlineUtc: startedAt.AddTicks(-1)));
        AssertEx.Throws<ArgumentException>(() => new ProxyShutdownSubsystemSummary(IsRunning: true, IsShuttingDown: false, ShutdownStartedAtUtc: startedAt, ShutdownDeadlineUtc: null));
        AssertEx.Throws<ArgumentException>(() => new ProxyShutdownSubsystemSummary(IsRunning: true, IsShuttingDown: false, ShutdownStartedAtUtc: null, ShutdownDeadlineUtc: deadline));
        AssertEx.Throws<ArgumentException>(() => new ProxyShutdownSubsystemSummary(IsRunning: false, IsShuttingDown: true, ShutdownStartedAtUtc: null, ShutdownDeadlineUtc: deadline));
        AssertEx.Throws<ArgumentException>(() => new ProxyShutdownSubsystemSummary(IsRunning: false, IsShuttingDown: true, ShutdownStartedAtUtc: startedAt, ShutdownDeadlineUtc: null));
        AssertEx.Throws<ArgumentException>(() => new ProxyShutdownSubsystemSummary(IsRunning: false, IsShuttingDown: true, ShutdownStartedAtUtc: startedAt, ShutdownDeadlineUtc: startedAt.AddTicks(-1)));
        AssertEx.Throws<ArgumentException>(() => BuildStatus(isShuttingDown: false, shutdownStartedAtUtc: startedAt, shutdownDeadlineUtc: null));
        AssertEx.Throws<ArgumentException>(() => BuildStatus(isShuttingDown: false, shutdownStartedAtUtc: null, shutdownDeadlineUtc: deadline));
        AssertEx.Throws<ArgumentException>(() => BuildStatus(isShuttingDown: true, shutdownStartedAtUtc: null, shutdownDeadlineUtc: deadline));
        AssertEx.Throws<ArgumentException>(() => BuildStatus(isShuttingDown: true, shutdownStartedAtUtc: startedAt, shutdownDeadlineUtc: null));
        AssertEx.Throws<ArgumentException>(() => BuildStatus(isShuttingDown: true, shutdownStartedAtUtc: startedAt, shutdownDeadlineUtc: startedAt.AddTicks(-1)));
        var source = new ProxyShutdownSummarySource(IsRunning: false, IsShuttingDown: true, ShutdownStartedAtUtc: startedAt, ShutdownDeadlineUtc: deadline);
        var summary = new ProxyShutdownSubsystemSummary(IsRunning: false, IsShuttingDown: true, ShutdownStartedAtUtc: startedAt, ShutdownDeadlineUtc: deadline);
        AssertEx.False(source.IsRunning);
        AssertEx.True(source.IsShuttingDown);
        AssertEx.Equal(startedAt, source.ShutdownStartedAtUtc);
        AssertEx.Equal(deadline, source.ShutdownDeadlineUtc);
        AssertEx.False(summary.IsRunning);
        AssertEx.True(summary.IsShuttingDown);
        AssertEx.Equal(startedAt, summary.ShutdownStartedAtUtc);
        AssertEx.Equal(deadline, summary.ShutdownDeadlineUtc);
        static ProxyStatus BuildStatus(bool isShuttingDown, DateTimeOffset? shutdownStartedAtUtc, DateTimeOffset? shutdownDeadlineUtc)
        {
            return new ProxyStatus(listenerLive: false, listenerName: null, endpoint: null, startedAt: null, stoppedAt: DateTimeOffset.UnixEpoch, lastError: null, isShuttingDown: isShuttingDown, shutdownStartedAtUtc: shutdownStartedAtUtc, shutdownDeadlineUtc: shutdownDeadlineUtc, configVersion: 1, configLoadedAtUtc: DateTimeOffset.UnixEpoch, configuredListeners: 0, configuredRoutes: 0, metrics: new ProxyMetrics().Snapshot(), upstreams: []);
        }
    }

    public static void SubsystemSummarySourceMappersCopyProjectedLists()
    {
        var listener = Listener();
        var activeListener = ListenerStatus(listener, ProxyListenerState.Active);
        var upstream = new ProxyUpstreamStatus(RouteName: "main", UpstreamName: "primary", Endpoint: "https://primary.internal", Scheme: "https", TlsCertificateValidationEnabled: true, SniHost: "primary.internal", HealthCheckEnabled: true, HealthState: UpstreamHealthState.Healthy, LastHealthCheckResult: "status_200", LastHealthCheckAtUtc: DateTimeOffset.UnixEpoch, ConsecutiveSuccesses: 2, ConsecutiveFailures: 0, SelectedRequests: 11, RequestFailures: 0);
        var listenerStatuses = new List<ProxyListenerStatus>
        {
            activeListener
        };
        var upstreamStatuses = new List<ProxyUpstreamStatus>
        {
            upstream
        };
        var runtimeListeners = ProxyRuntimeListenerSummarySourceMapper.FromSources(listenerStatuses);
        var upstreams = ProxyUpstreamSummarySourceMapper.FromStatusResponses(upstreamStatuses);
        var runtimeLimits = ProxyLimitSummarySourceMapper.FromSources(activeConnections: 1, activeTlsHandshakes: 2, activeHttp2Streams: 3, activeHttp3Streams: 4, activeUpstreamHttp3Streams: 5);
        listenerStatuses.Clear();
        upstreamStatuses.Clear();
        AssertEx.Equal(1, runtimeListeners.Count);
        AssertEx.False(runtimeListeners[0].IsQuic);
        AssertEx.Equal(ProxyListenerState.Active, runtimeListeners[0].State);
        AssertEx.False(runtimeListeners is ProxyRuntimeListenerSummarySource[], "Subsystem runtime listener sources should not expose a mutable array.");
        AssertEx.Equal(1, upstreams.Count);
        AssertEx.Equal(UpstreamHealthState.Healthy, upstreams[0].HealthState);
        AssertEx.True(upstreams[0].HealthCheckEnabled);
        AssertEx.False(upstreams is ProxyUpstreamSummarySource[], "Subsystem upstream sources should not expose a mutable array.");
        AssertEx.Equal(1, runtimeLimits.ActiveConnections);
        AssertEx.Equal(2, runtimeLimits.ActiveTlsHandshakes);
        AssertEx.Equal(3, runtimeLimits.ActiveHttp2Streams);
        AssertEx.Equal(4, runtimeLimits.ActiveHttp3Streams);
        AssertEx.Equal(5, runtimeLimits.ActiveUpstreamHttp3Streams);
        AssertEx.Throws<ArgumentNullException>(() => ProxyRuntimeListenerSummarySourceMapper.FromSources(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyUpstreamSummarySourceMapper.FromStatusResponses(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRuntimeListenerSummarySourceMapper.FromSources([null!]));
        AssertEx.Throws<ArgumentNullException>(() => ProxyUpstreamSummarySourceMapper.FromStatusResponses([null!]));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyLimitSummarySourceMapper.FromSources(activeConnections: -1, activeTlsHandshakes: 2, activeHttp2Streams: 3, activeHttp3Streams: 4, activeUpstreamHttp3Streams: 5));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyLimitSummarySourceMapper.FromSources(activeConnections: 1, activeTlsHandshakes: -1, activeHttp2Streams: 3, activeHttp3Streams: 4, activeUpstreamHttp3Streams: 5));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyLimitSummarySourceMapper.FromSources(activeConnections: 1, activeTlsHandshakes: 2, activeHttp2Streams: -1, activeHttp3Streams: 4, activeUpstreamHttp3Streams: 5));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyLimitSummarySourceMapper.FromSources(activeConnections: 1, activeTlsHandshakes: 2, activeHttp2Streams: 3, activeHttp3Streams: -1, activeUpstreamHttp3Streams: 5));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyLimitSummarySourceMapper.FromSources(activeConnections: 1, activeTlsHandshakes: 2, activeHttp2Streams: 3, activeHttp3Streams: 4, activeUpstreamHttp3Streams: -1));
    }

    public static void SubsystemSummaryBuilderCountsNarrowListenerRouteAndUpstreamSources()
    {
        ProxyConfiguredListenerSummarySource[] configuredListeners = [new(Enabled: true, Http1Enabled: true, Http2Enabled: false, Http3EnabledForTraffic: true), new(Enabled: false, Http1Enabled: true, Http2Enabled: true, Http3EnabledForTraffic: false)];
        ProxyRuntimeListenerSummarySource[] runtimeListeners = [new(IsQuic: false, ProxyListenerState.Active), new(IsQuic: true, ProxyListenerState.Active), new(IsQuic: false, ProxyListenerState.Failed), new(IsQuic: false, ProxyListenerState.Draining)];
        ProxyRouteSummarySource[] routes = [new("main", IsProxyRoute: true, CacheEnabled: true, HasHttp3Upstream: false), new("main", IsProxyRoute: false, CacheEnabled: false, HasHttp3Upstream: true), new("api", IsProxyRoute: true, CacheEnabled: false, HasHttp3Upstream: false)];
        ProxyUpstreamSummarySource[] upstreams = [new(UpstreamHealthState.Healthy, HealthCheckEnabled: true, CircuitBreakerEnabled: true, CircuitBreakerRuntimeState.Closed), new(UpstreamHealthState.Unhealthy, HealthCheckEnabled: true, CircuitBreakerEnabled: true, CircuitBreakerRuntimeState.Open), new(UpstreamHealthState.Unknown, HealthCheckEnabled: false, CircuitBreakerEnabled: false, CircuitBreakerRuntimeState.Disabled)];
        var listeners = ProxySubsystemSummaryBuilder.BuildListeners(configuredListeners, runtimeListeners);
        var routeSummary = ProxySubsystemSummaryBuilder.BuildRoutes(routes);
        var upstreamSummary = ProxySubsystemSummaryBuilder.BuildUpstreams(upstreams);
        var circuitSummary = ProxySubsystemSummaryBuilder.BuildCircuits(upstreams);
        var protocolSummary = ProxySubsystemSummaryBuilder.BuildProtocols(configuredListeners, clientHttp3Enabled: true, clientHttp3Ready: false, routes);
        AssertEx.Equal(2, listeners.Configured);
        AssertEx.Equal(1, listeners.Enabled);
        AssertEx.Equal(2, listeners.Active);
        AssertEx.Equal(1, listeners.Failed);
        AssertEx.Equal(1, listeners.Draining);
        AssertEx.Equal(1, listeners.Http1Enabled);
        AssertEx.Equal(0, listeners.Http2Enabled);
        AssertEx.Equal(1, listeners.Http3Enabled);
        AssertEx.Equal(1, listeners.QuicReady);
        AssertEx.Equal(2, routeSummary.Sites);
        AssertEx.Equal(3, routeSummary.Routes);
        AssertEx.Equal(2, routeSummary.ProxyRoutes);
        AssertEx.Equal(1, routeSummary.GeneratedRoutes);
        AssertEx.Equal(1, routeSummary.CacheEnabledRoutes);
        AssertEx.Equal(3, upstreamSummary.Total);
        AssertEx.Equal(1, upstreamSummary.Healthy);
        AssertEx.Equal(1, upstreamSummary.Unhealthy);
        AssertEx.Equal(1, upstreamSummary.UnknownHealth);
        AssertEx.Equal(2, upstreamSummary.HealthChecksEnabled);
        AssertEx.Equal(2, circuitSummary.Enabled);
        AssertEx.Equal(1, circuitSummary.Open);
        AssertEx.Equal(0, circuitSummary.HalfOpen);
        AssertEx.Equal(1, circuitSummary.Closed);
        AssertEx.True(protocolSummary.ClientHttp1Enabled);
        AssertEx.False(protocolSummary.ClientHttp2Enabled);
        AssertEx.True(protocolSummary.ClientHttp3Enabled);
        AssertEx.False(protocolSummary.ClientHttp3Ready);
        AssertEx.True(protocolSummary.UpstreamHttp3Configured);
    }

    public static void SubsystemSummaryBuilderClassifiesCertificateIssuesFromNarrowSources()
    {
        var now = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
        var source = new ProxyCertificateSummarySource(ReferencedCertificateIds: ["missing-cert", "expired-cert", "future-cert", "soon-cert"], LoadedCertificates: [new ProxyCertificateValiditySource("expired-cert", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc)), new ProxyCertificateValiditySource("future-cert", new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)), new ProxyCertificateValiditySource("soon-cert", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc))]);
        var summary = ProxySubsystemSummaryBuilder.BuildCertificates(source, now);
        AssertEx.Equal(4, summary.Configured);
        AssertEx.Equal(3, summary.Loaded);
        AssertEx.Equal(1, summary.MissingReferences);
        AssertEx.Equal(1, summary.Expired);
        AssertEx.Equal(1, summary.NotYetValid);
        AssertEx.Equal(1, summary.ExpiringSoon);
        AssertEx.NotNull(summary.LastIssue);
        AssertEx.Equal("certificate", summary.LastIssue!.Category);
        AssertEx.Equal("missing_reference", summary.LastIssue.Reason);
        AssertEx.Equal("missing-cert", summary.LastIssue.AffectedIdentity);
        AssertEx.Equal(now, summary.LastIssue.TimestampUtc);
    }

    public static void StatusReadinessReportsLogPersistenceFailureAsDegraded()
    {
        const string querySecret = "phase-49-query-secret";
        using var fixture = StatusFixture.Create();
        var listener = Listener();
        File.WriteAllText(Path.Combine(fixture.DataDirectory, "logs"), "not a directory");
        fixture.Store.Replace(Snapshot([listener], [StaticRoute()]));
        fixture.Runtime.ReplaceListeners([ListenerStatus(listener, ProxyListenerState.Active)], null);
        fixture.Writer.WriteAdminAudit(new ProxyAdminAuditEvent(DateTimeOffset.UtcNow, "GET", $"/admin/proxy/status?token={querySecret}", "127.0.0.1", "invalid", 403, false));
        var status = fixture.Controller().Get();
        var text = JsonSerializer.Serialize(status.Readiness) + JsonSerializer.Serialize(status.Subsystems);
        AssertEx.Equal("degraded", status.Readiness.State);
        AssertEx.True(status.Readiness.Reasons.Contains("log_persistence_degraded"), string.Join(",", status.Readiness.Reasons));
        AssertEx.Equal("degraded", status.Subsystems.Logs.State);
        AssertEx.False(text.Contains(querySecret, StringComparison.Ordinal), text);
        AssertEx.False(text.Contains(AdminToken, StringComparison.Ordinal), text);
        AssertEx.False(text.Contains("Authorization", StringComparison.OrdinalIgnoreCase), text);
        AssertEx.False(text.Contains("Cookie", StringComparison.OrdinalIgnoreCase), text);
    }

    public static void StatusReadinessReportsNoActiveListenerAsNotReady()
    {
        var stoppedAtUtc = new DateTimeOffset(2026, 6, 10, 10, 35, 0, TimeSpan.Zero);
        using var fixture = StatusFixture.Create(new FixedTimeProvider(stoppedAtUtc));
        var listener = Listener();
        fixture.Store.Replace(Snapshot([listener], [StaticRoute()]));
        fixture.Runtime.ReplaceListeners([ListenerStatus(listener, ProxyListenerState.Failed)], null);
        var status = fixture.Controller().Get();
        AssertEx.Equal("not_ready", status.Readiness.State);
        AssertEx.True(status.Readiness.Reasons.Contains("no_active_listeners"), string.Join(",", status.Readiness.Reasons));
        AssertEx.Equal(1, status.Subsystems.Listeners.Failed);
        AssertEx.Equal(0, status.Subsystems.Listeners.Active);
        AssertEx.Equal(stoppedAtUtc, status.StoppedAt);
    }

    public static void StatusReadinessReportsFailedListenerReloadWithoutLosingActiveConfig()
    {
        using var fixture = StatusFixture.Create();
        var listener = Listener();
        var active = ListenerStatus(listener, ProxyListenerState.Active);
        fixture.Store.Replace(Snapshot([listener], [StaticRoute()]));
        fixture.Runtime.ReplaceListeners([active], ProxyListenerReloadResult.Failed(DateTimeOffset.UtcNow, added: 0, removed: 0, changed: 0, unchanged: 1, changes: [], errors: ["raw bind failure that should not be copied into readiness"]));
        var status = fixture.Controller().Get();
        AssertEx.Equal("degraded", status.Readiness.State);
        AssertEx.True(status.Readiness.Reasons.Contains("last_listener_reload_failed"), string.Join(",", status.Readiness.Reasons));
        AssertEx.Equal(1, status.Readiness.ConfigGeneration);
        AssertEx.True(status.Subsystems.Config.Active);
        AssertEx.False(status.Subsystems.Config.LastListenerReloadSucceeded!.Value);
        AssertEx.Equal("listener_reload_failed", status.Subsystems.Config.LastListenerReloadReason);
        AssertEx.Equal(1, status.Subsystems.Listeners.Active);
    }

    public static void StatusReadinessSummarizesUnhealthyUpstreams()
    {
        using var fixture = StatusFixture.Create();
        var listener = Listener();
        var upstream = Upstream();
        var route = ProxyRoute(upstream, healthEnabled: true);
        fixture.Store.Replace(Snapshot([listener], [route]));
        fixture.Runtime.ReplaceListeners([ListenerStatus(listener, ProxyListenerState.Active)], null);
        fixture.Health.RecordHealthCheckResult(HealthTarget(route, upstream), HealthCheckSample.UnhealthyResult("status_500"), DateTimeOffset.UtcNow);
        var status = fixture.Controller().Get();
        AssertEx.Equal("degraded", status.Readiness.State);
        AssertEx.True(status.Readiness.Reasons.Contains("upstream_unhealthy"), string.Join(",", status.Readiness.Reasons));
        AssertEx.Equal(1, status.Subsystems.Upstreams.Total);
        AssertEx.Equal(1, status.Subsystems.Upstreams.Unhealthy);
        AssertEx.Equal(1, status.Subsystems.Upstreams.HealthChecksEnabled);
    }

    public static void StatusReadinessSummarizesOpenCircuits()
    {
        using var fixture = StatusFixture.Create();
        var listener = Listener();
        var upstream = Upstream(new RuntimeCircuitBreakerPolicy(true, 1, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30), 1, []));
        var route = ProxyRoute(upstream);
        fixture.Store.Replace(Snapshot([listener], [route]));
        fixture.Runtime.ReplaceListeners([ListenerStatus(listener, ProxyListenerState.Active)], null);
        var acquisition = fixture.Circuit.Acquire(CircuitBreakerStatusSourceMapper.FromUpstream(upstream));
        if (acquisition is not CircuitBreakerAcquisitionResult.AcceptedResult acceptedAcquisition)
        {
            throw new InvalidOperationException("Expected accepted circuit breaker acquisition.");
        }

        fixture.Circuit.RecordFailure(acceptedAcquisition.Lease, "connect_failure");
        var status = fixture.Controller().Get();
        AssertEx.Equal("degraded", status.Readiness.State);
        AssertEx.True(status.Readiness.Reasons.Contains("circuit_not_closed"), string.Join(",", status.Readiness.Reasons));
        AssertEx.Equal(1, status.Subsystems.Circuits.Enabled);
        AssertEx.Equal(1, status.Subsystems.Circuits.Open);
    }

    public static void StatusSubsystemsKeepUnsupportedHttp3FeaturesExplicit()
    {
        using var fixture = StatusFixture.Create();
        var listener = Listener();
        fixture.Store.Replace(Snapshot([listener], [StaticRoute()]));
        fixture.Runtime.ReplaceListeners([ListenerStatus(listener, ProxyListenerState.Active)], null);
        var protocols = fixture.Controller().Get().Subsystems.Protocols;
        AssertEx.False(protocols.ClientHttp3Enabled);
        AssertEx.True(protocols.UnsupportedHttp3Features.SequenceEqual(RuntimeHttp3UnsupportedFeatureCodes.StatusSummary), string.Join(",", protocols.UnsupportedHttp3Features));
    }

    public static void StatusProtocolSubsystemSummaryOwnsUnsupportedFeatureList()
    {
        var unsupportedFeatures = new List<string>
        {
            "datagrams",
            "webtransport"
        };
        var protocols = new ProxyProtocolSubsystemSummary(clientHttp1Enabled: true, clientHttp2Enabled: true, clientHttp3Enabled: false, clientHttp3Ready: false, upstreamHttp3Configured: true, unsupportedHttp3Features: unsupportedFeatures);
        unsupportedFeatures.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ProxyProtocolSubsystemSummary(clientHttp1Enabled: true, clientHttp2Enabled: true, clientHttp3Enabled: false, clientHttp3Ready: false, upstreamHttp3Configured: true, unsupportedHttp3Features: null!));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyProtocolSubsystemSummary(clientHttp1Enabled: true, clientHttp2Enabled: true, clientHttp3Enabled: false, clientHttp3Ready: false, upstreamHttp3Configured: true, unsupportedHttp3Features: [null!]));
        AssertEx.Equal(2, protocols.UnsupportedHttp3Features.Count);
        AssertEx.Equal("datagrams", protocols.UnsupportedHttp3Features[0]);
        AssertEx.Equal("webtransport", protocols.UnsupportedHttp3Features[1]);
        AssertEx.False(protocols.UnsupportedHttp3Features is string[], "Protocol subsystem summary should not expose a mutable array.");
    }

    public static void StatusProtocolSubsystemResponseOwnsUnsupportedFeatureList()
    {
        var unsupportedFeatures = new List<string>
        {
            "datagrams",
            "webtransport"
        };
        var response = new ProxyProtocolSubsystemSummaryResponse(clientHttp1Enabled: true, clientHttp2Enabled: true, clientHttp3Enabled: false, clientHttp3Ready: false, upstreamHttp3Configured: true, unsupportedHttp3Features: unsupportedFeatures);
        unsupportedFeatures.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ProxyProtocolSubsystemSummaryResponse(clientHttp1Enabled: true, clientHttp2Enabled: true, clientHttp3Enabled: false, clientHttp3Ready: false, upstreamHttp3Configured: true, unsupportedHttp3Features: null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyProtocolSubsystemSummaryResponseMapper.FromSummary(null!));
        AssertEx.Equal(2, response.UnsupportedHttp3Features.Count);
        AssertEx.Equal("datagrams", response.UnsupportedHttp3Features[0]);
        AssertEx.Equal("webtransport", response.UnsupportedHttp3Features[1]);
        AssertEx.False(response.UnsupportedHttp3Features is string[], "Protocol subsystem response should not expose a mutable array.");
    }

    public static void StatusReadinessReportsCertificateIssueSummaryWithoutSecrets()
    {
        const string missingCertificateId = "public-cert";
        using var fixture = StatusFixture.Create();
        var listener = Listener(RuntimeListenerTransport.Https, missingCertificateId, RuntimeListenerProtocols.Http1, RuntimeHttp3Enablement.Disabled);
        fixture.Store.Replace(Snapshot([listener], [StaticRoute()]));
        fixture.Runtime.ReplaceListeners([ListenerStatus(listener, ProxyListenerState.Active)], null);
        var status = fixture.Controller().Get();
        var text = JsonSerializer.Serialize(status.Readiness) + JsonSerializer.Serialize(status.Subsystems);
        AssertEx.Equal("degraded", status.Readiness.State);
        AssertEx.True(status.Readiness.Reasons.Contains("certificate_reference_missing"), string.Join(",", status.Readiness.Reasons));
        AssertEx.Equal(1, status.Subsystems.Certificates.MissingReferences);
        AssertEx.NotNull(status.Subsystems.Certificates.LastIssue);
        AssertEx.Equal("certificate", status.Subsystems.Certificates.LastIssue!.Category);
        AssertEx.Equal("missing_reference", status.Subsystems.Certificates.LastIssue.Reason);
        AssertEx.Equal(missingCertificateId, status.Subsystems.Certificates.LastIssue.AffectedIdentity);
        AssertEx.False(text.Contains(AdminToken, StringComparison.Ordinal), text);
        AssertEx.False(text.Contains("Authorization", StringComparison.OrdinalIgnoreCase), text);
        AssertEx.False(text.Contains("Cookie", StringComparison.OrdinalIgnoreCase), text);
        AssertEx.False(text.Contains("private-key", StringComparison.OrdinalIgnoreCase), text);
    }

    public static void StatusReadinessReportsAcmeLastIssueWithoutRawErrorSummary()
    {
        const string rawErrorSecret = "phase-50-acme-token-secret";
        using var fixture = StatusFixture.Create();
        var listener = Listener();
        var acme = new RuntimeAcmeOptions(true, true, "https://acme.invalid/directory", [], true, "acme", 30, 720, 60, [new RuntimeAcmeCertificateOptions("acme-cert", true, ["example.test"], 30)]);
        fixture.Store.Replace(Snapshot([listener], [StaticRoute()], acme: acme));
        fixture.Runtime.ReplaceListeners([ListenerStatus(listener, ProxyListenerState.Active)], null);
        fixture.Acme.Upsert(new AcmeCertificateLifecycleStatus("acme-cert", true, ["example.test"], false, "acme", null, null, null, DateTimeOffset.UtcNow.AddMinutes(-2), null, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(30), "failed", $"raw ACME exception with {rawErrorSecret} and C:\\private\\account.json"));
        var status = fixture.Controller().Get();
        var text = JsonSerializer.Serialize(status.Readiness) + JsonSerializer.Serialize(status.Subsystems);
        AssertEx.Equal("degraded", status.Readiness.State);
        AssertEx.True(status.Readiness.Reasons.Contains("acme_degraded"), string.Join(",", status.Readiness.Reasons));
        AssertEx.True(status.Subsystems.Acme.Enabled);
        AssertEx.Equal(1, status.Subsystems.Acme.Configured);
        AssertEx.Equal(1, status.Subsystems.Acme.Failed);
        AssertEx.Equal(1, status.Subsystems.Acme.RenewalBackoff);
        AssertEx.NotNull(status.Subsystems.Acme.LastIssue);
        AssertEx.Equal("acme", status.Subsystems.Acme.LastIssue!.Category);
        AssertEx.Equal("failed", status.Subsystems.Acme.LastIssue.Reason);
        AssertEx.Equal("acme-cert", status.Subsystems.Acme.LastIssue.AffectedIdentity);
        AssertEx.False(text.Contains(rawErrorSecret, StringComparison.Ordinal), text);
        AssertEx.False(text.Contains("account.json", StringComparison.OrdinalIgnoreCase), text);
        AssertEx.False(text.Contains("example.test", StringComparison.OrdinalIgnoreCase), text);
        AssertEx.False(text.Contains("Authorization", StringComparison.OrdinalIgnoreCase), text);
        AssertEx.False(text.Contains("Cookie", StringComparison.OrdinalIgnoreCase), text);
    }

    public static void StatusReadinessReportsRuntimePreflightWarningsWithoutSecrets()
    {
        const string secret = "phase-52-preflight-secret";
        using var fixture = StatusFixture.Create();
        var listener = Listener();
        fixture.Store.Replace(Snapshot([listener], [StaticRoute()]));
        fixture.Runtime.ReplaceListeners([ListenerStatus(listener, ProxyListenerState.Active)], null);
        var preflight = new ProxyRuntimePreflightService(new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = fixture.DataDirectory }), new ProxyDataDirectoryPathSafety(), new RuntimePreflightProbe(path => path.EndsWith("logs", StringComparison.OrdinalIgnoreCase) ? ProxyRuntimeDirectoryProbeResult.Probed(created: false, canRead: true, canWrite: false) : ProxyRuntimeDirectoryProbeResult.Probed(created: false, canRead: true, canWrite: true)), TimeProvider.System);
        preflight.RunStartupChecks();
        var status = fixture.Controller(preflight).Get();
        var text = JsonSerializer.Serialize(status.Readiness) + JsonSerializer.Serialize(status.RuntimePreflight);
        AssertEx.Equal("degraded", status.Readiness.State);
        AssertEx.True(status.Readiness.Reasons.Contains("runtime_preflight_degraded"), string.Join(",", status.Readiness.Reasons));
        AssertEx.Equal("degraded", status.RuntimePreflight.State);
        AssertEx.True(status.RuntimePreflight.Reasons.Contains("directory_not_writable"), string.Join(",", status.RuntimePreflight.Reasons));
        AssertEx.False(text.Contains(secret, StringComparison.Ordinal), text);
        AssertEx.False(text.Contains(fixture.DataDirectory, StringComparison.OrdinalIgnoreCase), text);
        AssertEx.False(text.Contains("Authorization", StringComparison.OrdinalIgnoreCase), text);
        AssertEx.False(text.Contains("Cookie", StringComparison.OrdinalIgnoreCase), text);
    }

    private sealed class RuntimePreflightProbe : IProxyRuntimeDirectoryProbe
    {
        private readonly Func<string, ProxyRuntimeDirectoryProbeResult> _probe;
        public RuntimePreflightProbe(Func<string, ProxyRuntimeDirectoryProbeResult> probe)
        {
            _probe = probe;
        }

        public ProxyRuntimeDirectoryProbeResult Probe(string path, bool createIfMissing)
        {
            return _probe(path);
        }
    }

    private static ProxyConfigurationSnapshot Snapshot(IReadOnlyList<RuntimeListener> listeners, IReadOnlyList<RuntimeRoute> routes, IReadOnlyDictionary<string, RuntimeCertificate>? certificates = null, RuntimeAcmeOptions? acme = null)
    {
        return new ProxyConfigurationSnapshot(1, DateTimeOffset.UtcNow, "tests", [], new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("tests", "tests/config", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", "tests/config/proxy.json"), [], [], []), new RuntimeAdminSecurityOptions([], true, true, AdminToken, "MDRAVA_ADMIN_TOKEN", "configured", 100), acme ?? new RuntimeAcmeOptions(false, true, "", [], false, "acme", 30, 720, 60, []), Timeouts(), new RuntimeConnectionLimits(100, 16, 1024), new RuntimeObservabilityOptions(true, 100, new RuntimeLogPersistenceOptions(true, true, 1_048_576, 8)), new RuntimeLimits(4096, 128, 240, 30, 32768, 128, 8192, 104857600, 8192, TimeSpan.FromSeconds(15)), new RuntimeForwardedHeadersOptions(true, []), certificates ?? new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), listeners, routes);
    }

    private static RuntimeListener Listener(RuntimeListenerTransport transport = RuntimeListenerTransport.Http, string? defaultCertificateId = null, RuntimeListenerProtocols protocols = RuntimeListenerProtocols.Http1, RuntimeHttp3Enablement http3Enablement = RuntimeHttp3Enablement.Default)
    {
        return new RuntimeListener("main", "127.0.0.1", 18080, true, transport, defaultCertificateId, [], 512, 32768, 32768, 8192, 8192, protocols, http3Enablement, RuntimeHttp3AltSvcOptions.Disabled, RuntimeHttp2Limits.Default);
    }

    private static ProxyListenerStatus ListenerStatus(RuntimeListener listener, ProxyListenerState state)
    {
        var identity = listener.Identity;
        return new ProxyListenerStatus(listener.Name, identity.Key, identity.BindKey, "tcp", listener.Address, listener.Port, listener.Transport.ToString().ToLowerInvariant(), listener.Transport == RuntimeListenerTransport.Https, listener.Protocols.ToConfigText(), listener.Http3.ToStatus(), listener.Http2Limits.MaxConcurrentStreams, listener.Http2Limits.MaxHeaderListBytes, listener.Http2Limits.MaxFrameSize, state, ActiveConnections: 0, state == ProxyListenerState.Active ? DateTimeOffset.UtcNow : null, state == ProxyListenerState.Active ? null : DateTimeOffset.UtcNow, state == ProxyListenerState.Failed ? "bind_failed" : null);
    }

    private static RuntimeRoute StaticRoute(RuntimeCachePolicy? cache = null)
    {
        return BaseRoute(RuntimeRouteAction.StaticResponse, [], cache ?? RuntimeCachePolicy.Disabled);
    }

    private static RuntimeRoute ProxyRoute(RuntimeUpstream upstream, bool healthEnabled = false)
    {
        return BaseRoute(RuntimeRouteAction.Proxy, [upstream], RuntimeCachePolicy.Disabled, healthEnabled);
    }

    private static RuntimeRoute BaseRoute(RuntimeRouteAction action, IReadOnlyList<RuntimeUpstream> upstreams, RuntimeCachePolicy cache, bool healthEnabled = false)
    {
        return new RuntimeRoute("main", "*", "/", action, "round-robin", new RuntimeHealthCheckOptions(healthEnabled, "/health", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1, 1), upstreams, new RuntimeHttpsRedirectPolicy(false, 308, null), new RuntimeCanonicalHostPolicy(false, "", 308), RuntimeHeaderPolicy.Empty, new RuntimePathRewritePolicy("", "", ""), new RuntimeRedirectPolicy(308, "", "", true), new RuntimeStaticResponse(200, "text/plain; charset=utf-8", "ok"), new RuntimeMaintenancePolicy(false, null, "text/plain; charset=utf-8", "Service Unavailable"), cache, new RuntimeRouteResolvedOptions(104857600, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), true), SiteName: "main", Retry: RuntimeRetryPolicy.Disabled);
    }

    private static RuntimeUpstream Upstream(RuntimeCircuitBreakerPolicy? circuitBreaker = null, string scheme = "http", string protocol = RuntimeUpstreamProtocol.Http1)
    {
        return new RuntimeUpstream("main", "upstream", scheme, protocol, "127.0.0.1", 5000, 1, RuntimeUpstreamTlsOptions.Default, circuitBreaker ?? RuntimeCircuitBreakerPolicy.Disabled);
    }

    private static UpstreamHealthCheckTarget HealthTarget(RuntimeRoute route, RuntimeUpstream upstream)
    {
        return new UpstreamHealthCheckTarget(route.Name, upstream.Name, upstream.Endpoint, upstream.Identity, UpstreamTransportEndpointMapper.FromUpstream(upstream), route.HealthCheck.Path, route.HealthCheck.Interval, route.HealthCheck.Timeout, route.HealthCheck.HealthyThreshold, route.HealthCheck.UnhealthyThreshold);
    }

    private static RuntimeCachePolicy CachePolicy()
    {
        return new RuntimeCachePolicy(true, 1024 * 1024, 16 * 1024 * 1024, TimeSpan.FromSeconds(60), true, [], [200], ["GET", "HEAD"]);
    }

    private static RuntimeTimeouts Timeouts()
    {
        return new RuntimeTimeouts(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private static RuntimeHttp3SupportProjection UnknownHttp3()
    {
        return new RuntimeHttp3SupportProjection("unknown", QuicListenerSupported: false, QuicConnectionSupported: false, "disabled", "disabled", EnabledForTraffic: false, QuicListenerReady: false, AltSvcConfigured: false, AltSvcActive: false, AltSvcMaxAgeSeconds: null, "not_configured", UdpQuicListenerIdentityModeled: true, "client_http3_default_enabled_for_eligible_tls_proxy_listeners");
    }

    private static ProxySubsystemSummaries HealthyReadinessSubsystems()
    {
        return new ProxySubsystemSummaries(new ProxyConfigSubsystemSummary(true, 77, DateTimeOffset.UnixEpoch, true, "listener_reload_succeeded"), new ProxyListenerSubsystemSummary(1, 1, 1, 0, 0, 1, 0, 0, 0), new ProxyRouteSubsystemSummary(1, 1, 1, 0, 0), new ProxyCertificateSubsystemSummary(0, 0, 0, 0, 0, 0, null), new ProxyAcmeSubsystemSummary(false, 0, 0, 0, 0, null), new ProxyUpstreamSubsystemSummary(1, 1, 0, 0, 0), new ProxyCacheSubsystemSummary(false, 0, 0, 0), new ProxyCircuitSubsystemSummary(0, 0, 0, 0), new ProxyLimitSubsystemSummary(4096, 0, 16, 0, 0, 0, 0, 30), new ProxyLogSubsystemSummary(true, true, ProxyStatusText.Healthy, "ok"), new ProxyShutdownSubsystemSummary(true, false, null, null), new ProxyProtocolSubsystemSummary(true, false, false, false, false, []));
    }

    private sealed class StatusFixture : IDisposable
    {
        private StatusFixture(string dataDirectory, TimeProvider timeProvider)
        {
            DataDirectory = dataDirectory;
            Metrics = new ProxyMetrics();
            Store = new ProxyConfigurationStore();
            Runtime = new ProxyRuntimeState(timeProvider);
            Pool = new UpstreamConnectionPool(new UpstreamConnectionFactory(), Metrics, timeProvider);
            Circuit = new CircuitBreakerStore(Metrics, TimeProvider.System);
            Health = new UpstreamHealthStore(Metrics, Pool, Circuit);
            Cache = new ResponseCacheStore(TimeProvider.System);
            Acme = new AcmeCertificateStatusStore();
            Writer = new ProxyPersistentLogWriter(new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = dataDirectory }), new ProxyLogPersistenceSettingsReader(new ProxyConfigurationLogPersistenceSettingsSource(Store)), NullLogger<ProxyPersistentLogWriter>.Instance, timeProvider);
        }

        public string DataDirectory { get; }
        public ProxyMetrics Metrics { get; }
        public ProxyConfigurationStore Store { get; }
        public ProxyRuntimeState Runtime { get; }
        public UpstreamConnectionPool Pool { get; }
        public CircuitBreakerStore Circuit { get; }
        public UpstreamHealthStore Health { get; }
        public ResponseCacheStore Cache { get; }
        public AcmeCertificateStatusStore Acme { get; }
        public ProxyPersistentLogWriter Writer { get; }

        public static StatusFixture Create(TimeProvider? timeProvider = null)
        {
            var path = Path.Combine(Path.GetTempPath(), $"mdrava-status-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new StatusFixture(path, timeProvider ?? TimeProvider.System);
        }

        public ProxyStatusController Controller(ProxyRuntimePreflightService? preflight = null)
        {
            var statusOperations = ProxyStatusOperationFactory.Create(Runtime, Metrics, Store, Health, logPersistenceStore: Writer, cacheStore: Cache, acmeStatusStore: Acme, preflightService: preflight);
            return new ProxyStatusController(new ProxyStatusAdministrationService(statusOperations));
        }

        public ProxyStatusInputReader InputReader(ProxyRuntimePreflightService? preflight = null, TimeProvider? timeProvider = null)
        {
            return new ProxyStatusInputReader(Runtime, Metrics, Store, new ProxyStatusUpstreamHealthReader(Store, Health), FixedConfigLintOperations.Instance, Writer, new ProxyCacheStatusReader(new ProxyCacheStatusConfigurationSource(Store), new ProxyCacheRuntimeStatusSource(Cache)), new ProxyAcmeCertificateLifecycleStatusSource(Acme), preflight is null ? FixedRuntimePreflightSource.Instance : preflight, TestHttp3PlatformSupport.SupportedSource, timeProvider ?? TimeProvider.System);
        }

        public void Dispose()
        {
            Pool.Dispose();
            try
            {
                if (Directory.Exists(DataDirectory))
                {
                    Directory.Delete(DataDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private sealed class FixedConfigLintOperations : IProxyConfigLintOperations
    {
        public static FixedConfigLintOperations Instance { get; } = new();
        public ConfigLintStatus LastActiveStatus => ConfigLintStatus.Empty;

        public ConfigLintResult LintActive()
        {
            return EmptyResult();
        }

        public ConfigLintResult LintSubmitted(ConfigLintRequest? request)
        {
            return EmptyResult();
        }

        private static ConfigLintResult EmptyResult()
        {
            return ConfigLintResult.Completed(DateTimeOffset.UnixEpoch, [], []);
        }
    }

    private sealed class FixedRuntimePreflightSource : IProxyStatusRuntimePreflightSource
    {
        public static FixedRuntimePreflightSource Instance { get; } = new();

        public ProxyRuntimePreflightStatus ReadRuntimePreflight()
        {
            return ProxyRuntimePreflightStatus.Unknown;
        }
    }

    private sealed class FixedStatusConfigurationSource : IProxyStatusConfigurationSource
    {
        private readonly ProxyStatusConfigurationSourceSet? _configuration;
        public FixedStatusConfigurationSource(ProxyStatusConfigurationSourceSet? configuration)
        {
            _configuration = configuration;
        }

        public ProxyStatusConfigurationReadResult ReadConfiguration()
        {
            return _configuration is null ? ProxyStatusConfigurationReadResult.MissingConfiguration : ProxyStatusConfigurationReadResult.Available(_configuration);
        }
    }

    private sealed class CapturingStatusUpstreamHealthSource : IProxyStatusUpstreamHealthSource
    {
        public IReadOnlyList<ProxyUpstreamHealthSource> LastUpstreams { get; private set; } = [];

        public IReadOnlyList<ProxyUpstreamStatus> ReadUpstreams(IReadOnlyList<ProxyUpstreamHealthSource> upstreams)
        {
            LastUpstreams = upstreams;
            return upstreams.Select(static upstream => new ProxyUpstreamStatus(upstream.HealthState.RouteName, upstream.HealthState.UpstreamName, upstream.HealthState.UpstreamEndpoint, upstream.Scheme, upstream.ValidateCertificate, upstream.EffectiveSniHost, upstream.HealthCheckEnabled, UpstreamHealthState.Unknown, LastHealthCheckResult: null, LastHealthCheckAtUtc: null, ConsecutiveSuccesses: 0, ConsecutiveFailures: 0, SelectedRequests: 0, RequestFailures: 0, upstream.Protocol, upstream.Weight, CircuitBreakerStatus.Disabled(CircuitBreakerPolicyInput.Disabled))).ToArray();
        }
    }

    private static ProxyUpstreamStatus CreateUpstreamStatus(ProxyUpstreamStatus source, string? upstreamName = null)
    {
        return new ProxyUpstreamStatus(source.RouteName, upstreamName ?? source.UpstreamName, source.Endpoint, source.Scheme, source.TlsCertificateValidationEnabled, source.SniHost, source.HealthCheckEnabled, source.HealthState, source.LastHealthCheckResult, source.LastHealthCheckAtUtc, source.ConsecutiveSuccesses, source.ConsecutiveFailures, source.SelectedRequests, source.RequestFailures, source.Protocol, source.Weight, source.CircuitBreaker);
    }

    private static ProxyUpstreamStatusResponse CreateUpstreamStatusResponse(ProxyUpstreamStatusResponse source, string? upstreamName = null)
    {
        return new ProxyUpstreamStatusResponse(source.RouteName, upstreamName ?? source.UpstreamName, source.Endpoint, source.Scheme, source.TlsCertificateValidationEnabled, source.SniHost, source.HealthCheckEnabled, source.HealthState, source.LastHealthCheckResult, source.LastHealthCheckAtUtc, source.ConsecutiveSuccesses, source.ConsecutiveFailures, source.SelectedRequests, source.RequestFailures, source.Protocol, source.Weight, source.CircuitBreaker);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
