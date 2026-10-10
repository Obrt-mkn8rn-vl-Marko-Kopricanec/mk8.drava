using System.Net;
using System.Net.Sockets;
using System.Text;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Connections;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;
using Mk8.Drava.CompatibilityTests.LegacyApi.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class MetricsTests
{
    private const string AdminToken = "phase-17-admin-token";
    public static async Task MetricsEndpointIsProtectedByAdminAuthAsync()
    {
        var store = CreateStoreWithAdminAuthentication();
        var audit = new AdminAuditStore(SilentLogPersistenceStore.Instance);
        var metrics = new ProxyMetrics();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = RuntimeMetricsOptions.FixedAdminEndpointPath;
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        var middleware = ProxyAdminAuthenticationTestFactory.CreateMiddleware(_ => Task.CompletedTask, store, audit, metrics);
        await middleware.InvokeAsync(context).ConfigureAwait(false);
        AssertEx.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        AssertEx.Equal(1L, metrics.Snapshot().AdminAuth.Failures);
    }

    public static void MetricsEndpointReturnsPrometheusText()
    {
        using var fixture = MetricsFixture.Create();
        var store = CreateStore();
        var controller = new ProxyMetricsController(new ProxyMetricsAdministrationService(CreateExportProvider(store, fixture)));
        var content = (ContentResult)controller.Get();
        var text = AssertEx.NotNull(content.Content);
        AssertEx.Equal(PrometheusMetricsExporter.ContentType, content.ContentType);
        AssertEx.True(text.Contains("# HELP mdrava_requests_total", StringComparison.Ordinal));
        AssertEx.True(text.Contains("# TYPE mdrava_requests_total counter", StringComparison.Ordinal));
    }

    public static void GeneratedFailureResponseMetricsClassifyCountedStatuses()
    {
        var metrics = new ProxyMetrics();
        metrics.GeneratedFailureResponse(502);
        metrics.GeneratedFailureResponse(504);
        metrics.GeneratedFailureResponse(408);
        var snapshot = metrics.Snapshot();
        AssertEx.Equal(1L, snapshot.GeneratedResponses.BadGatewayResponses);
        AssertEx.Equal(1L, snapshot.GeneratedResponses.GatewayTimeoutResponses);
    }

    public static void MetricsSnapshotCopiesCollectionInputs()
    {
        var requestFailures = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["ConnectFailure"] = 2
        };
        var requestsByRoute = new List<ProxyRequestSeriesSnapshot>
        {
            new("site-a", "route-a", "proxy", "2xx", 3)
        };
        var retrySkipped = new List<ProxyRetrySkippedSnapshot>
        {
            new("unsafe_method", 4)
        };
        var upstreamSelections = new List<ProxyUpstreamSelectionSnapshot>
        {
            new("route-a", "upstream-a", "http", RuntimeUpstreamProtocol.Http1, 5)
        };
        var http2ProtocolErrors = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["stream_error"] = 6
        };
        var upstreamHttp3ProtocolErrors = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["goaway"] = 7
        };
        var http3RequestsByOutcome = new List<ProxyHttp3RequestOutcomeSnapshot>
        {
            new("GET", "proxied", "2xx", 8)
        };
        var http3RejectedRequests = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["malformed"] = 9
        };
        var http3ProtocolErrors = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["qpack"] = 10
        };
        var configLintFindings = new List<ProxyConfigLintFindingMetricSnapshot>
        {
            new("warning", "route_shadowed", 11)
        };
        var routeMatchFailures = new List<ProxyRouteDryRunFailureSnapshot>
        {
            new("no_route", 12)
        };
        var snapshot = new ProxyMetricsSnapshot(ClientConnections: new ProxyClientConnectionMetricsSnapshot(Accepted: 0, Active: 0, ClosedByIdleTimeout: 0, ClosedByMaxRequests: 0), Traffic: new ProxyTrafficMetricsSnapshot(Requests: 0, BytesRead: 0, BytesWritten: 0), UpstreamForwarding: new ProxyUpstreamForwardingMetricsSnapshot(Successes: 0, Failures: 0, BodyRelayFailures: 0), ClientFailures: new ProxyClientFailureMetricsSnapshot(ParseErrors: 0, BodyRelayFailures: 0, RequestHeadTimeouts: 0, RequestBodyTimeouts: 0, PrematureDisconnects: 0, DownstreamWriteTimeouts: 0), Rejections: new ProxyRejectionMetricsSnapshot(ClientConnectionAdmissionRejections: 0, RateLimitedRequests: 0, RateLimitedUpgrades: 0, RequestBodySizeRejections: 0, ParserLimitRejections: 0, MalformedRequests: 0, UnsupportedRequestFraming: 0), UpstreamFailureReasons: new ProxyUpstreamFailureMetricsSnapshot(ConnectFailures: 0, ConnectTimeouts: 0, ResponseHeadTimeouts: 0, ResponseBodyTimeouts: 0, MalformedResponses: 0, PrematureDisconnects: 0, RequestFailures: 0), GeneratedResponses: new ProxyGeneratedResponseMetricsSnapshot(BadGatewayResponses: 0, GatewayTimeoutResponses: 0), Tls: new ProxyTlsMetricsSnapshot(HandshakeAttempts: 0, HandshakeSuccesses: 0, HandshakeFailures: 0, HandshakeTimeouts: 0, NoCertificateForSniFailures: 0, ActiveHandshakes: 0, HandshakeAdmissionRejections: 0), UpstreamPool: new ProxyUpstreamPoolMetricsSnapshot(ConnectionsOpened: 0, ConnectionsReused: 0, ConnectionsDiscarded: 0, IdleConnections: 0, ActiveConnections: 0), Upgrades: new ProxyUpgradeMetricsSnapshot(RequestsReceived: 0, RequestsSucceeded: 0, RequestsRejected: 0, UpstreamFailures: 0), Tunnels: new ProxyTunnelMetricsSnapshot(Active: 0, Total: 0, IdleTimeouts: 0, BytesClientToUpstream: 0, BytesUpstreamToClient: 0, RelayFailures: 0), UpstreamSelections: new ProxyUpstreamSelectionMetricsSnapshot(Total: 0, ByUpstream: upstreamSelections.Select(static item => item)), Health: new ProxyHealthMetricsSnapshot(NoHealthyUpstreamFailures: 0, ChecksAttempted: 0, ChecksSucceeded: 0, ChecksFailed: 0, UpstreamTransitions: 0), Diagnostics: new ProxyDiagnosticsMetricsSnapshot(RequestIdsGenerated: 0, AccessLogsEmitted: 0, RecentDiagnosticsOverwritten: 0), RequestClassifications: new ProxyRequestClassificationMetricsSnapshot(FailuresByKind: requestFailures, ByRoute: requestsByRoute.Select(static item => item)), ConfigReloads: new ProxyConfigReloadMetricsSnapshot(Successes: 0, Failures: 0), AdminAuth: new ProxyAdminAuthMetricsSnapshot(Successes: 0, Failures: 0), AcmeRenewals: new ProxyAcmeRenewalMetricsSnapshot(Attempts: 0, Successes: 0, Failures: 0), Resilience: new ProxyResilienceMetricsSnapshot(RetryAttempts: 0, RetryExhausted: 0, RetrySkipped: retrySkipped.Select(static item => item), CircuitOpened: 0, CircuitHalfOpened: 0, CircuitClosed: 0, CircuitRejections: 0, NoAvailableUpstreamFailures: 0), Listeners: new ProxyListenerMetricsSnapshot(ReloadAttempts: 0, ReloadSuccesses: 0, ReloadFailures: 0, ReloadAdded: 0, ReloadRemoved: 0, ReloadChanged: 0, ReloadUnchanged: 0, StartFailures: 0, Drains: 0, ActiveListeners: 0), Http2: new ProxyHttp2MetricsSnapshot(AcceptedConnections: 0, Requests: 0, ActiveStreams: 0, ProtocolErrors: http2ProtocolErrors), UpstreamHttp2: new ProxyUpstreamHttp2MetricsSnapshot(Requests: 0, AlpnFailures: 0, ProtocolErrors: 0), UpstreamHttp3: new ProxyUpstreamHttp3MetricsSnapshot(Requests: 0, ConnectionAttempts: 0, ConnectionSuccesses: 0, ConnectionFailures: 0, PoolConnectionsOpened: 0, PoolConnectionsReused: 0, PoolConnectionsClosed: 0, StreamLimitRejections: 0, ActiveConnections: 0, ActiveStreams: 0, ProtocolErrors: upstreamHttp3ProtocolErrors), Http3: new ProxyHttp3MetricsSnapshot(AcceptedConnections: 0, ActiveConnections: 0, Requests: 0, ProxiedRequests: 0, GeneratedResponses: 0, ActiveStreams: 0, StreamResets: 0, StreamedResponses: 0, ActiveResponseStreams: 0, ResponseBytesSent: 0, RequestBodyBytesReceived: 0, ResponseStreamResets: 0, AltSvcEmitted: 0, AltSvcSuppressed: 0, RequestsByOutcome: http3RequestsByOutcome.Select(static item => item), RejectedRequests: http3RejectedRequests, ProtocolErrors: http3ProtocolErrors, QuicListenerStartSuccesses: 0, QuicListenerStartFailures: 0, ActiveQuicListeners: 0), ConfigLint: new ProxyConfigLintMetricsSnapshot(Runs: 0, Findings: configLintFindings.Select(static item => item)), RouteDiagnostics: new ProxyRouteDiagnosticsMetricsSnapshot(DryRuns: 0, DryRunFailures: routeMatchFailures.Select(static item => item)));
        requestFailures.Clear();
        requestsByRoute.Clear();
        retrySkipped.Clear();
        upstreamSelections.Clear();
        http2ProtocolErrors.Clear();
        upstreamHttp3ProtocolErrors.Clear();
        http3RequestsByOutcome.Clear();
        http3RejectedRequests.Clear();
        http3ProtocolErrors.Clear();
        configLintFindings.Clear();
        routeMatchFailures.Clear();
        VerifyMetricSnapshotCopies(snapshot);
        VerifyMetricSnapshotGuards(requestFailures);
        VerifyMetricsApiCollectionViews(snapshot);
    }

    private static void VerifyMetricSnapshotCopies(ProxyMetricsSnapshot snapshot)
    {
        AssertEx.Equal(2L, snapshot.RequestClassifications.FailuresByKind["ConnectFailure"]);
        AssertEx.Equal("route-a", snapshot.RequestClassifications.ByRoute[0].Route);
        AssertEx.Equal("unsafe_method", snapshot.Resilience.RetrySkipped[0].Reason);
        AssertEx.Equal("upstream-a", snapshot.UpstreamSelections.ByUpstream[0].Upstream);
        AssertEx.Equal(6L, snapshot.Http2.ProtocolErrors["stream_error"]);
        AssertEx.Equal(7L, snapshot.UpstreamHttp3.ProtocolErrors["goaway"]);
        AssertEx.Equal("proxied", snapshot.Http3.RequestsByOutcome[0].Outcome);
        AssertEx.Equal(9L, snapshot.Http3.RejectedRequests["malformed"]);
        AssertEx.Equal(10L, snapshot.Http3.ProtocolErrors["qpack"]);
        AssertEx.Equal("route_shadowed", snapshot.ConfigLint.Findings[0].Code);
        AssertEx.Equal("no_route", snapshot.RouteDiagnostics.DryRunFailures[0].Reason);
        AssertEx.False(snapshot.RequestClassifications.FailuresByKind is Dictionary<string, long>);
        AssertEx.False(snapshot.RequestClassifications.ByRoute is ProxyRequestSeriesSnapshot[]);
        AssertEx.False(snapshot.Resilience.RetrySkipped is ProxyRetrySkippedSnapshot[]);
        AssertEx.False(snapshot.UpstreamSelections.ByUpstream is ProxyUpstreamSelectionSnapshot[]);
        AssertEx.False(snapshot.Http2.ProtocolErrors is Dictionary<string, long>);
        AssertEx.False(snapshot.UpstreamHttp3.ProtocolErrors is Dictionary<string, long>);
        AssertEx.False(snapshot.Http3.RequestsByOutcome is ProxyHttp3RequestOutcomeSnapshot[]);
        AssertEx.False(snapshot.Http3.RejectedRequests is Dictionary<string, long>);
        AssertEx.False(snapshot.Http3.ProtocolErrors is Dictionary<string, long>);
        AssertEx.False(snapshot.ConfigLint.Findings is ProxyConfigLintFindingMetricSnapshot[]);
        AssertEx.False(snapshot.RouteDiagnostics.DryRunFailures is ProxyRouteDryRunFailureSnapshot[]);
    }

    private static void VerifyMetricSnapshotGuards(Dictionary<string, long> requestFailures)
    {
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyClientConnectionMetricsSnapshot(Accepted: -1, Active: 0, ClosedByIdleTimeout: 0, ClosedByMaxRequests: 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyTrafficMetricsSnapshot(Requests: 0, BytesRead: -1, BytesWritten: 0));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRequestClassificationMetricsSnapshot(requestFailures, [null!]));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyRequestSeriesSnapshot(Site: "site-a", Route: "route-a", Action: "proxy", StatusClass: "2xx", Count: -1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyRequestClassificationMetricsSnapshot(new Dictionary<string, long>(StringComparer.Ordinal) { ["ConnectFailure"] = -1 }, []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyResilienceMetricsSnapshot(RetryAttempts: 0, RetryExhausted: 0, RetrySkipped: [null!], CircuitOpened: 0, CircuitHalfOpened: 0, CircuitClosed: 0, CircuitRejections: 0, NoAvailableUpstreamFailures: 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyRetrySkippedSnapshot(Reason: "unsafe_method", Count: -1));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyUpstreamSelectionMetricsSnapshot(Total: 0, ByUpstream: [null!]));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyUpstreamSelectionMetricsSnapshot(Total: -1, ByUpstream: []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyUpstreamSelectionSnapshot(Route: "route-a", Upstream: "upstream-a", Scheme: "https", Protocol: "http2", Count: -1));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyHttp3MetricsSnapshot(AcceptedConnections: 0, ActiveConnections: 0, Requests: 0, ProxiedRequests: 0, GeneratedResponses: 0, ActiveStreams: 0, StreamResets: 0, StreamedResponses: 0, ActiveResponseStreams: 0, ResponseBytesSent: 0, RequestBodyBytesReceived: 0, ResponseStreamResets: 0, AltSvcEmitted: 0, AltSvcSuppressed: 0, RequestsByOutcome: [null!], RejectedRequests: new Dictionary<string, long>(StringComparer.Ordinal), ProtocolErrors: new Dictionary<string, long>(StringComparer.Ordinal), QuicListenerStartSuccesses: 0, QuicListenerStartFailures: 0, ActiveQuicListeners: 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyTlsMetricsSnapshot(HandshakeAttempts: 0, HandshakeSuccesses: 0, HandshakeFailures: 0, HandshakeTimeouts: 0, NoCertificateForSniFailures: 0, ActiveHandshakes: -1, HandshakeAdmissionRejections: 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyTunnelMetricsSnapshot(Active: 0, Total: 0, IdleTimeouts: 0, BytesClientToUpstream: -1, BytesUpstreamToClient: 0, RelayFailures: 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyHttp3RequestOutcomeSnapshot(Method: "GET", Outcome: "proxied", StatusClass: "2xx", Count: -1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyHttp2MetricsSnapshot(AcceptedConnections: 0, Requests: 0, ActiveStreams: 0, ProtocolErrors: new Dictionary<string, long>(StringComparer.Ordinal) { ["stream_error"] = -1 }));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyUpstreamHttp3MetricsSnapshot(Requests: 0, ConnectionAttempts: 0, ConnectionSuccesses: 0, ConnectionFailures: 0, PoolConnectionsOpened: 0, PoolConnectionsReused: 0, PoolConnectionsClosed: 0, StreamLimitRejections: 0, ActiveConnections: 0, ActiveStreams: 0, ProtocolErrors: new Dictionary<string, long>(StringComparer.Ordinal) { ["goaway"] = -1 }));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyHttp3MetricsSnapshot(AcceptedConnections: 0, ActiveConnections: 0, Requests: 0, ProxiedRequests: 0, GeneratedResponses: 0, ActiveStreams: 0, StreamResets: 0, StreamedResponses: 0, ActiveResponseStreams: 0, ResponseBytesSent: 0, RequestBodyBytesReceived: 0, ResponseStreamResets: 0, AltSvcEmitted: 0, AltSvcSuppressed: 0, RequestsByOutcome: [], RejectedRequests: new Dictionary<string, long>(StringComparer.Ordinal) { ["malformed"] = -1 }, ProtocolErrors: new Dictionary<string, long>(StringComparer.Ordinal), QuicListenerStartSuccesses: 0, QuicListenerStartFailures: 0, ActiveQuicListeners: 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyHttp3MetricsSnapshot(AcceptedConnections: 0, ActiveConnections: 0, Requests: 0, ProxiedRequests: 0, GeneratedResponses: 0, ActiveStreams: 0, StreamResets: 0, StreamedResponses: 0, ActiveResponseStreams: 0, ResponseBytesSent: 0, RequestBodyBytesReceived: 0, ResponseStreamResets: 0, AltSvcEmitted: 0, AltSvcSuppressed: 0, RequestsByOutcome: [], RejectedRequests: new Dictionary<string, long>(StringComparer.Ordinal), ProtocolErrors: new Dictionary<string, long>(StringComparer.Ordinal) { ["qpack"] = -1 }, QuicListenerStartSuccesses: 0, QuicListenerStartFailures: 0, ActiveQuicListeners: 0));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigLintMetricsSnapshot(Runs: 0, Findings: [null!]));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyConfigLintMetricsSnapshot(Runs: -1, Findings: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsMetricsSnapshot(DryRuns: 0, DryRunFailures: [null!]));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyRouteDiagnosticsMetricsSnapshot(DryRuns: -1, DryRunFailures: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDryRunFailureSnapshot(null!, 1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyRouteDryRunFailureSnapshot("no_route", -1));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigLintFindingMetricSnapshot(null!, "route_shadowed", 1));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigLintFindingMetricSnapshot("warning", " ", 1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyConfigLintFindingMetricSnapshot("warning", "route_shadowed", -1));
    }

    private static void VerifyMetricsApiCollectionViews(ProxyMetricsSnapshot snapshot)
    {
        var response = ProxyMetricsSnapshotResponseMapper.FromSnapshot(snapshot);
        AssertEx.False(response.RequestFailuresByKind is Dictionary<string, long>, "Metrics API request failures should not expose a mutable dictionary.");
        AssertEx.False(response.RequestsByRoute is ProxyRequestSeriesSnapshotResponse[], "Metrics API request series should not expose a mutable array.");
        AssertEx.False(response.RetrySkipped is ProxyRetrySkippedSnapshotResponse[], "Metrics API retry skipped series should not expose a mutable array.");
        AssertEx.False(response.UpstreamSelectionsByUpstream is ProxyUpstreamSelectionSnapshotResponse[], "Metrics API upstream selection series should not expose a mutable array.");
        AssertEx.False(response.Http2ProtocolErrors is Dictionary<string, long>, "Metrics API HTTP/2 protocol errors should not expose a mutable dictionary.");
        AssertEx.False(response.UpstreamHttp3ProtocolErrors is Dictionary<string, long>, "Metrics API upstream HTTP/3 protocol errors should not expose a mutable dictionary.");
        AssertEx.False(response.Http3RequestsByOutcome is ProxyHttp3RequestOutcomeSnapshotResponse[], "Metrics API HTTP/3 outcome series should not expose a mutable array.");
        AssertEx.False(response.Http3RejectedRequests is Dictionary<string, long>, "Metrics API HTTP/3 rejected requests should not expose a mutable dictionary.");
        AssertEx.False(response.Http3ProtocolErrors is Dictionary<string, long>, "Metrics API HTTP/3 protocol errors should not expose a mutable dictionary.");
        AssertEx.False(response.ConfigLintFindings is ProxyConfigLintFindingMetricSnapshotResponse[], "Metrics API config lint findings should not expose a mutable array.");
        AssertEx.False(response.RouteMatchDryRunFailures is ProxyRouteDryRunFailureSnapshotResponse[], "Metrics API route dry-run failures should not expose a mutable array.");
    }

    public static void MetricsExportInputSourceReadsActiveRuntimeFacts()
    {
        using var fixture = MetricsFixture.Create();
        var store = CreateStore();
        var source = CreateExportInputSource(new ProxyConfigurationMetricsExportConfigurationSource(store), store, fixture);
        var result = source.ReadInput();
        AssertEx.True(result is ProxyMetricsExportInputReadResult.AvailableResult);
        var input = ((ProxyMetricsExportInputReadResult.AvailableResult)result).Input;
        AssertEx.Equal(0L, input.Metrics.Traffic.Requests);
        AssertEx.Equal(0L, input.Metrics.UpstreamForwarding.Failures);
        AssertEx.Equal(0, input.DefaultEnabledHttp3ListenerCount);
        AssertEx.False(input.Http3RequestBodyStreamingEnabled);
        AssertEx.Equal(0, input.CacheStatus.Routes.Count);
    }

    public static void MetricsExportConfigurationMappersReadNarrowRuntimeFacts()
    {
        var metrics = new RuntimeMetricsOptions(true, RuntimeMetricsOptions.FixedAdminEndpointPath, true, false, true, false);
        var listener = Listener(RuntimeListenerTransport.Https, "metrics-cert", RuntimeListenerProtocols.Http3);
        var upstream = new RuntimeUpstream("metrics", "h3", "https", RuntimeUpstreamProtocol.Http3, "upstream.internal", 443, 1, RuntimeUpstreamTlsOptions.Default);
        var route = Route(RuntimeCachePolicy.Disabled).WithUpstreams([upstream]);
        var labelOptions = ProxyMetricsExportLabelOptionsMapper.FromMetrics(metrics);
        RuntimeListener[] listeners = [listener];
        RuntimeRoute[] routes = [route];
        var http3Facts = ProxyMetricsExportHttp3FactsMapper.FromSources(new SinglePassEnumerable<RuntimeListener>(listeners), new SinglePassEnumerable<RuntimeRoute>(routes));
        AssertEx.False(labelOptions.IncludePerRouteLabels);
        AssertEx.True(labelOptions.IncludePerUpstreamLabels);
        AssertEx.Equal(1, http3Facts.DefaultEnabledListenerCount);
        AssertEx.True(http3Facts.RequestBodyStreamingEnabled);
        AssertEx.True(http3Facts.UpstreamMultiplexingConfigured);
    }

    public static void MetricsExportConfigurationMapperConsumesNamedFactsWithoutRuntimeConfiguration()
    {
        var labelOptions = new ProxyMetricsExportLabelOptions(IncludePerRouteLabels: false, IncludePerUpstreamLabels: true);
        var http3Facts = new ProxyMetricsExportHttp3Facts(DefaultEnabledListenerCount: 2, RequestBodyStreamingEnabled: true, UpstreamMultiplexingConfigured: false);
        var configuration = ProxyMetricsExportConfigurationMapper.FromSources(metricsEnabled: true, labelOptions, http3Facts);
        AssertEx.True(configuration.MetricsEnabled);
        AssertEx.False(configuration.LabelOptions.IncludePerRouteLabels);
        AssertEx.True(configuration.LabelOptions.IncludePerUpstreamLabels);
        AssertEx.Equal(2, configuration.Http3Facts.DefaultEnabledListenerCount);
        AssertEx.True(configuration.Http3Facts.RequestBodyStreamingEnabled);
        AssertEx.False(configuration.Http3Facts.UpstreamMultiplexingConfigured);
    }

    public static void MetricsExportConfigurationMapperRejectsNullNamedFacts()
    {
        var labelOptions = new ProxyMetricsExportLabelOptions(IncludePerRouteLabels: false, IncludePerUpstreamLabels: true);
        var http3Facts = new ProxyMetricsExportHttp3Facts(DefaultEnabledListenerCount: 2, RequestBodyStreamingEnabled: true, UpstreamMultiplexingConfigured: false);
        AssertEx.Throws<ArgumentNullException>(() => ProxyMetricsExportConfigurationMapper.FromSources(true, null!, http3Facts));
        AssertEx.Throws<ArgumentNullException>(() => ProxyMetricsExportConfigurationMapper.FromSources(true, labelOptions, null!));
    }

    public static void MetricsExportHttp3CountsRejectNegativeFacts()
    {
        var cacheStatus = ProxyCacheStatus.FromSources(entryCount: 0, approximateBytes: 0, hitCount: 0, missCount: 0, storeCount: 0, evictionCount: 0, storeRejectionCount: 0, lastClearedAtUtc: null, lastClearReason: null, rejections: [], routes: []);
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyMetricsExportHttp3Facts(DefaultEnabledListenerCount: -1, RequestBodyStreamingEnabled: false, UpstreamMultiplexingConfigured: false));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyMetricsExportInput(Metrics: new ProxyMetrics().Snapshot(), IncludePerRouteLabels: false, IncludePerUpstreamLabels: false, DefaultEnabledHttp3ListenerCount: -1, Http3RequestBodyStreamingEnabled: false, UpstreamHttp3MultiplexingConfigured: false, cacheStatus, UpstreamHealth: [], AcmeCertificates: []));
        var facts = new ProxyMetricsExportHttp3Facts(DefaultEnabledListenerCount: 0, RequestBodyStreamingEnabled: false, UpstreamMultiplexingConfigured: false);
        AssertEx.Equal(0, facts.DefaultEnabledListenerCount);
    }

    public static void MetricsExportInputMapperCopiesSourceLists()
    {
        var upstreamHealth = new List<ProxyUpstreamStatus>
        {
            new("route-a", "upstream-a", "127.0.0.1:5000", "http", TlsCertificateValidationEnabled: true, SniHost: null, HealthCheckEnabled: true, UpstreamHealthState.Healthy, LastHealthCheckResult: "success", LastHealthCheckAtUtc: DateTimeOffset.UnixEpoch, ConsecutiveSuccesses: 1, ConsecutiveFailures: 0, SelectedRequests: 3, RequestFailures: 0)
        };
        var acmeCertificates = new List<AcmeCertificateLifecycleStatus>
        {
            new("home-cert", Enabled: true, Domains: ["home.test"], Active: true, Source: "acme", NotBeforeUtc: DateTimeOffset.UnixEpoch, NotAfterUtc: DateTimeOffset.UnixEpoch.AddDays(30), RenewalDueAtUtc: DateTimeOffset.UnixEpoch.AddDays(20), LastAttemptAtUtc: DateTimeOffset.UnixEpoch.AddHours(1), LastSucceededAtUtc: DateTimeOffset.UnixEpoch.AddHours(1), LastFailedAtUtc: null, NextAttemptNotBeforeUtc: null, LastResult: "success", ErrorSummary: null)
        };
        var cacheStatus = ProxyCacheStatus.FromSources(entryCount: 0, approximateBytes: 0, hitCount: 0, missCount: 0, storeCount: 0, evictionCount: 0, storeRejectionCount: 0, lastClearedAtUtc: null, lastClearReason: null, rejections: [], routes: []);
        var input = ProxyMetricsExportInputMapper.FromSources(new ProxyMetrics().Snapshot(), new ProxyMetricsExportLabelOptions(IncludePerRouteLabels: true, IncludePerUpstreamLabels: true), new ProxyMetricsExportHttp3Facts(DefaultEnabledListenerCount: 1, RequestBodyStreamingEnabled: true, UpstreamMultiplexingConfigured: true), cacheStatus, upstreamHealth, acmeCertificates);
        upstreamHealth.Clear();
        acmeCertificates.Clear();
        AssertEx.Equal("route-a", input.UpstreamHealth[0].RouteName);
        AssertEx.Equal("upstream-a", input.UpstreamHealth[0].UpstreamName);
        AssertEx.Equal("home-cert", input.AcmeCertificates[0].CertificateId);
        AssertEx.Equal("home.test", input.AcmeCertificates[0].Domains[0]);
        AssertEx.True(input.IncludePerRouteLabels);
        AssertEx.True(input.IncludePerUpstreamLabels);
        AssertEx.Equal(1, input.DefaultEnabledHttp3ListenerCount);
        AssertEx.True(input.Http3RequestBodyStreamingEnabled);
        AssertEx.True(input.UpstreamHttp3MultiplexingConfigured);
        AssertEx.False(input.UpstreamHealth is ProxyUpstreamStatus[]);
        AssertEx.False(input.AcmeCertificates is AcmeCertificateLifecycleStatus[]);
    }

    public static void MetricsExportInputMapperRejectsNullNamedFacts()
    {
        var metrics = new ProxyMetrics().Snapshot();
        var labelOptions = new ProxyMetricsExportLabelOptions(IncludePerRouteLabels: true, IncludePerUpstreamLabels: true);
        var http3Facts = new ProxyMetricsExportHttp3Facts(DefaultEnabledListenerCount: 1, RequestBodyStreamingEnabled: true, UpstreamMultiplexingConfigured: true);
        var cacheStatus = ProxyCacheStatus.FromSources(entryCount: 0, approximateBytes: 0, hitCount: 0, missCount: 0, storeCount: 0, evictionCount: 0, storeRejectionCount: 0, lastClearedAtUtc: null, lastClearReason: null, rejections: [], routes: []);
        IReadOnlyList<ProxyUpstreamStatus> upstreamHealth = [];
        IReadOnlyList<AcmeCertificateLifecycleStatus> acmeCertificates = [];
        AssertEx.Throws<ArgumentNullException>(() => ProxyMetricsExportInputMapper.FromSources(metrics, null!, http3Facts, cacheStatus, upstreamHealth, acmeCertificates));
        AssertEx.Throws<ArgumentNullException>(() => ProxyMetricsExportInputMapper.FromSources(metrics, labelOptions, null!, cacheStatus, upstreamHealth, acmeCertificates));
    }

    public static void MetricsEndpointReturnsNotFoundWhenMetricsDisabled()
    {
        using var fixture = MetricsFixture.Create();
        var store = CreateStore(new ProxyOperationalOptions { Metrics = new ProxyMetricsOptions { Enabled = false } });
        var controller = new ProxyMetricsController(new ProxyMetricsAdministrationService(CreateExportProvider(store, fixture)));
        var result = (NotFoundResult)controller.Get();
        AssertEx.Equal(StatusCodes.Status404NotFound, result.StatusCode);
    }

    public static void MetricsExportResultNamesAvailableAndUnavailableStates()
    {
        var unavailable = ProxyMetricsExportResult.Unavailable;
        var exported = ProxyMetricsExportResult.Exported("metrics text", PrometheusMetricsExporter.ContentType);
        AssertEx.True(unavailable is not ProxyMetricsExportResult.ExportedResult);
        AssertEx.True(exported is ProxyMetricsExportResult.ExportedResult);
        AssertEx.Equal("metrics text", ((ProxyMetricsExportResult.ExportedResult)exported).Content);
        AssertEx.Equal(PrometheusMetricsExporter.ContentType, ((ProxyMetricsExportResult.ExportedResult)exported).ContentType);
    }

    public static void MetricsExportAvailabilityRequiresActiveEnabledConfig()
    {
        var missing = new ProxyMetricsExportAvailabilityService(new FixedMetricsExportAvailabilityReader(false, false)).GetAvailability();
        var disabled = new ProxyMetricsExportAvailabilityService(new FixedMetricsExportAvailabilityReader(true, false)).GetAvailability();
        var available = new ProxyMetricsExportAvailabilityService(new FixedMetricsExportAvailabilityReader(true, true)).GetAvailability();
        var fromState = ProxyMetricsExportAvailabilityResult.FromState(new ProxyMetricsExportAvailabilityState(HasActiveConfiguration: true, MetricsExportEnabled: true));
        AssertEx.False(missing.Available);
        AssertEx.False(missing.HasActiveConfiguration);
        AssertEx.False(missing.MetricsExportEnabled);
        AssertEx.False(disabled.Available);
        AssertEx.True(disabled.HasActiveConfiguration);
        AssertEx.False(disabled.MetricsExportEnabled);
        AssertEx.True(available.Available);
        AssertEx.True(fromState.Available);
        AssertEx.True(fromState.HasActiveConfiguration);
        AssertEx.True(fromState.MetricsExportEnabled);
    }

    public static void MetricsExportAvailabilityReaderNamesMissingConfiguration()
    {
        var reader = new ProxyMetricsExportAvailabilityReader(MissingMetricsExportConfigurationSource.Instance);
        var state = reader.Read();
        AssertEx.Equal(ProxyMetricsExportAvailabilityState.MissingConfiguration, state);
        AssertEx.False(state.HasActiveConfiguration);
        AssertEx.False(state.MetricsExportEnabled);
    }

    public static void MetricsExportAvailabilityStateMapsConfiguration()
    {
        var configuration = ProxyMetricsExportConfigurationMapper.FromSources(metricsEnabled: true, new ProxyMetricsExportLabelOptions(IncludePerRouteLabels: false, IncludePerUpstreamLabels: false), new ProxyMetricsExportHttp3Facts(DefaultEnabledListenerCount: 0, RequestBodyStreamingEnabled: false, UpstreamMultiplexingConfigured: false));
        var state = ProxyMetricsExportAvailabilityState.FromConfiguration(configuration);
        AssertEx.True(state.HasActiveConfiguration);
        AssertEx.True(state.MetricsExportEnabled);
    }

    public static async Task MetricsIncludeRequestCountersAfterProxiedRequestAsync()
    {
        var text = await RunProxiedRequestAndExportAsync("GET /metrics-check HTTP/1.1\r\nHost: metrics.test\r\nConnection: close\r\n\r\n").ConfigureAwait(false);
        AssertEx.True(text.Contains("mdrava_requests_total 1", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("mdrava_route_requests_total", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("site=\"metrics\"", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("route=\"metrics\"", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("action=\"proxy\"", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("status_class=\"2xx\"", StringComparison.Ordinal), text);
    }

    public static void MetricsIncludeCacheCountersAfterCacheActivity()
    {
        using var fixture = MetricsFixture.Create();
        var cache = fixture.Cache;
        var store = CreateStoreWithRoute(Route(CachePolicy()));
        var route = store.Snapshot.Routes[0];
        var listener = store.Snapshot.Listeners[0];
        var request = Request("GET", "/cached", "cache.test");
        var response = Response("200 OK", []);
        AssertEx.True(cache.Get(Scope(route, listener), request, "/cached") is ProxyCacheLookupResult.MissResult);
        cache.Store(Scope(route, listener), request, "/cached", response, response.Headers, Encoding.ASCII.GetBytes("cached"));
        AssertEx.True(cache.Get(Scope(route, listener), request, "/cached") is ProxyCacheLookupResult.HitResult);
        var text = fixture.Export(store.Snapshot);
        AssertEx.True(text.Contains("mdrava_cache_hits_total 1", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("mdrava_cache_misses_total 1", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("mdrava_cache_stores_total 1", StringComparison.Ordinal), text);
    }

    public static async Task MetricsIncludeReloadCountersAsync()
    {
        using var temp = TemporaryDirectory.Create();
        ConfigurationTests.WriteSite(temp.Path, "reload.json", 18080, 15000);
        using var fixture = MetricsFixture.Create();
        var store = new ProxyConfigurationStore();
        var service = new ProxyConfigurationReloadService(CreateLoader(temp.Path), store, store, fixture.Cache, fixture.Metrics, ActivatingProxyListenerReloadApplier.Instance, SilentProxyConfigurationReloadEventSink.Instance, TestHttp3PlatformSupport.ProjectionSource);
        var first = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(first);
        ConfigurationTests.WriteCustomSite(temp.Path, "broken.json", "{ nope");
        var second = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Failed(second);
        var text = fixture.Export(store.Snapshot);
        AssertEx.True(text.Contains("mdrava_config_reloads_total{result=\"success\"} 1", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("mdrava_config_reloads_total{result=\"failure\"} 1", StringComparison.Ordinal), text);
    }

    public static async Task MetricsDoNotExposeRawRequestDetailsAsync()
    {
        var text = await RunProxiedRequestAndExportAsync("GET /private/path?token=super-secret-token HTTP/1.1\r\nHost: metrics.test\r\nAuthorization: Bearer request-secret\r\nConnection: close\r\n\r\n").ConfigureAwait(false);
        AssertEx.False(text.Contains("/private/path", StringComparison.Ordinal));
        AssertEx.False(text.Contains("super-secret-token", StringComparison.Ordinal));
        AssertEx.False(text.Contains("request-secret", StringComparison.Ordinal));
        AssertEx.False(text.Contains("metrics.test", StringComparison.OrdinalIgnoreCase));
        AssertEx.False(text.Contains("127.0.0.1", StringComparison.Ordinal));
    }

    public static async Task MetricsFailureMatrixDoesNotExposeAuthorizationCookieOrQuerySecretsAsync()
    {
        var text = await RunFailedRequestAndExportAsync("GET /missing/path?token=query-secret HTTP/1.1\r\nHost: other.test\r\nAuthorization: Bearer auth-secret\r\nCookie: session=cookie-secret\r\nConnection: close\r\n\r\n").ConfigureAwait(false);
        AssertEx.True(text.Contains("mdrava_requests_total 1", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("status_class=\"4xx\"", StringComparison.Ordinal), text);
        AssertEx.False(text.Contains("/missing/path", StringComparison.Ordinal));
        AssertEx.False(text.Contains("query-secret", StringComparison.Ordinal));
        AssertEx.False(text.Contains("auth-secret", StringComparison.Ordinal));
        AssertEx.False(text.Contains("cookie-secret", StringComparison.Ordinal));
        AssertEx.False(text.Contains("other.test", StringComparison.OrdinalIgnoreCase));
    }

    public static void PublicMetricsExposureIsDisabledByDefault()
    {
        var snapshot = CreateStore().Snapshot;
        AssertEx.True(snapshot.Metrics.Enabled);
        AssertEx.Equal(RuntimeMetricsOptions.FixedAdminEndpointPath, snapshot.Metrics.EndpointPath);
        AssertEx.True(snapshot.Metrics.ProtectedByAdminAuth);
        AssertEx.False(snapshot.Metrics.PublicMetricsEnabled);
    }

    public static void InvalidMetricsConfigIsRejected()
    {
        var failures = ProxyOperationalOptionsValidationRules.Validate(new ProxyOperationalOptions { Metrics = new ProxyMetricsOptions { PublicMetricsEnabled = true } }, static _ => null, new Mk8.Drava.Application.INF.Configuration.ProxyAdminUrlPolicy(), new ProxyRelativeStoragePathPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy(), new ProxyForwardedHeadersAddressPolicy());
        AssertEx.True(failures.Any(static failure => failure.Contains("PublicMetricsEnabled", StringComparison.Ordinal)), string.Join("; ", failures));
    }

    public static void MetricLabelsAreBoundedAndSanitized()
    {
        using var fixture = MetricsFixture.Create();
        var store = CreateStore();
        var rawSite = "site\nsecret";
        var rawRoute = "route\"with/slash/and/query?token=secret";
        fixture.Metrics.RequestCompleted(rawSite, rawRoute, "proxy\r\nbad", 200);
        var text = fixture.Export(store.Snapshot);
        AssertEx.False(text.Contains(rawSite, StringComparison.Ordinal));
        AssertEx.False(text.Contains(rawRoute, StringComparison.Ordinal));
        AssertEx.False(text.Contains("token=secret", StringComparison.Ordinal));
        AssertEx.True(text.Contains("site_secret", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("route_with_slash_and_query_token_secret", StringComparison.Ordinal), text);
    }

    private static async Task<string> RunProxiedRequestAndExportAsync(string request)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        WriteMetricsSite(temp.Path, proxyPort, upstreamPort);
        var upstreamTask = RunSingleUpstreamAsync(upstreamPort, "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 7\r\n\r\nmetrics", timeout.Token);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var response = await SendSingleRequestAsync(proxyPort, request, timeout.Token).ConfigureAwait(false);
            AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
            await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var provider = host.Services.GetRequiredService<IProxyMetricsExportProvider>();
            return ExportedMetricsText(provider);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<string> RunFailedRequestAndExportAsync(string request)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        WriteMetricsSite(temp.Path, proxyPort, upstreamPort, host: "metrics.test");
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var response = await SendSingleRequestAsync(proxyPort, request, timeout.Token).ConfigureAwait(false);
            AssertEx.True(response.Contains("404 Not Found", StringComparison.Ordinal), response);
            var provider = host.Services.GetRequiredService<IProxyMetricsExportProvider>();
            return ExportedMetricsText(provider);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static string ExportedMetricsText(IProxyMetricsExportProvider provider)
    {
        var result = provider.Export();
        if (result is ProxyMetricsExportResult.ExportedResult exported)
        {
            return exported.Content;
        }

        throw new InvalidOperationException("Expected metrics export to be available.");
    }

    private static async Task<string> RunSingleUpstreamAsync(int upstreamPort, string response, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, upstreamPort);
        listener.Start();
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            await using var streamDisposal = stream.ConfigureAwait(false);
            var request = await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).ConfigureAwait(false);
            return request;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> SendSingleRequestAsync(int proxyPort, string request, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort, cancellationToken).ConfigureAwait(false);
        var stream = client.GetStream();
        await using var streamDisposal = stream.ConfigureAwait(false);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken).ConfigureAwait(false);
        return await ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadToEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (true)
        {
            var bytesRead = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static async Task<string> ReadRequestHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var total = 0;
        while (total < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(total, 1), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            total += bytesRead;
            if (total >= 4 && buffer[total - 4] == '\r' && buffer[total - 3] == '\n' && buffer[total - 2] == '\r' && buffer[total - 1] == '\n')
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(buffer, 0, total);
    }

    private static void WriteMetricsSite(string dataDirectory, int proxyPort, int upstreamPort, string host = "*")
    {
        ConfigurationTests.WriteCustomSite(dataDirectory, "metrics.json", $$"""
            {
              "name": "metrics",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}}
                }
              ],
              "host": "{{host}}",
              "pathPrefix": "/",
              "upstreams": [
                {
                  "name": "local-test",
                  "address": "127.0.0.1",
                  "port": {{upstreamPort}}
                }
              ]
            }
            """);
    }

    private static IHost BuildProxyHost(string dataDirectory)
    {
        return Host.CreateDefaultBuilder().ConfigureAppConfiguration(builder =>
        {
            builder.Sources.Clear();
            builder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Mdrava:DataDirectory"] = dataDirectory });
        }).ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices((context, services) => services.AddProxyDataPlane(context.Configuration)).Build();
    }

    private static ProxyConfigurationLoader CreateLoader(string dataDirectory)
    {
        var provider = new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = dataDirectory });
        return new ProxyConfigurationLoader(provider, new ProxyDataDirectoryBootstrapper(provider), new SiteConfigurationParser(), new Mk8.Drava.Application.INF.Configuration.ProxyAdminUrlPolicy(), new ProxyEndpointAddressPolicy(), new ProxyRelativeStoragePathPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy(), new ProxyForwardedHeadersAddressPolicy(), NullLogger<ProxyConfigurationLoader>.Instance, TimeProvider.System);
    }

    private static ProxyConfigurationStore CreateStore(ProxyOperationalOptions? operationalOptions = null)
    {
        var options = operationalOptions ?? new ProxyOperationalOptions();
        var store = new ProxyConfigurationStore();
        store.Replace(ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(new ProxyOptions(), options, ProxyAdminSecurityTokenPolicy.Resolve(options.Admin, static _ => null), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), 1, DateTimeOffset.UtcNow, "tests", [], Discovery()));
        return store;
    }

    private static ProxyMetricsExportProvider CreateExportProvider(ProxyConfigurationStore store, MetricsFixture fixture)
    {
        var configurationSource = new ProxyConfigurationMetricsExportConfigurationSource(store);
        return new ProxyMetricsExportProvider(CreateExportInputSource(configurationSource, store, fixture), fixture.Exporter, new ProxyMetricsExportAvailabilityService(new ProxyMetricsExportAvailabilityReader(configurationSource)));
    }

    private static ProxyMetricsExportInputSource CreateExportInputSource(IProxyMetricsExportConfigurationSource configurationSource, ProxyConfigurationStore store, MetricsFixture fixture)
    {
        return new ProxyMetricsExportInputSource(configurationSource, fixture.Metrics, new ProxyCacheStatusReader(new ProxyCacheStatusConfigurationSource(store), new ProxyCacheRuntimeStatusSource(fixture.Cache)), new ProxyStatusUpstreamHealthReader(store, fixture.Health), new ProxyAcmeCertificateLifecycleStatusSource(fixture.Acme));
    }

    private static ProxyConfigurationStore CreateStoreWithAdminAuthentication()
    {
        return CreateStore(new ProxyOperationalOptions { Admin = new ProxyAdminOptions { RequireAuthentication = true, Token = AdminToken } });
    }

    private static ProxyConfigurationStore CreateStoreWithRoute(RuntimeRoute route)
    {
        var operationalOptions = new ProxyOperationalOptions();
        var snapshot = ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(new ProxyOptions(), operationalOptions, ProxyAdminSecurityTokenPolicy.Resolve(operationalOptions.Admin, static _ => null), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), 1, DateTimeOffset.UtcNow, "tests", [], Discovery()).WithListenersAndRoutes([Listener()], [route]);
        var store = new ProxyConfigurationStore();
        store.Replace(snapshot);
        return store;
    }

    private static ProxyConfigurationDiscovery Discovery()
    {
        return new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("tests", "tests/config", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", "tests/config/proxy.json"), [], [], []);
    }

    private static RuntimeRoute Route(RuntimeCachePolicy cache)
    {
        return new RuntimeRoute("cache", "*", "/", RuntimeRouteAction.Proxy, "round-robin", new RuntimeHealthCheckOptions(false, "/health", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1, 1), [], new RuntimeHttpsRedirectPolicy(false, 308, null), new RuntimeCanonicalHostPolicy(false, "", 308), RuntimeHeaderPolicy.Empty, new RuntimePathRewritePolicy("", "", ""), new RuntimeRedirectPolicy(308, "", "", true), new RuntimeStaticResponse(200, "text/plain; charset=utf-8", ""), new RuntimeMaintenancePolicy(false, null, "text/plain; charset=utf-8", "Service Unavailable"), cache, new RuntimeRouteResolvedOptions(104857600, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), true), SiteName: "cache", Retry: RuntimeRetryPolicy.Disabled);
    }

    private static RuntimeCachePolicy CachePolicy()
    {
        return new RuntimeCachePolicy(true, 1024 * 1024, 16 * 1024 * 1024, TimeSpan.FromSeconds(60), true, [], [200], ["GET", "HEAD"]);
    }

    private static RuntimeListener Listener(RuntimeListenerTransport transport = RuntimeListenerTransport.Http, string? defaultCertificateId = null, RuntimeListenerProtocols protocols = RuntimeListenerProtocols.Http1)
    {
        return new RuntimeListener("main", "127.0.0.1", 18080, true, transport, defaultCertificateId, [], 512, 32768, 32768, 8192, 8192, protocols, RuntimeHttp3Enablement.Default, RuntimeHttp3AltSvcOptions.Disabled, RuntimeHttp2Limits.Default);
    }

    private static ProxyCacheRequestScope Scope(RuntimeRoute route, RuntimeListener listener)
    {
        return new ProxyCacheRequestScope(route.Name, route.Host, listener.Transport == RuntimeListenerTransport.Https ? "https" : "http", new ProxyCachePolicyFacts(route.Cache.Enabled, route.Cache.MaxEntryBytes, route.Cache.MaxTotalBytes, route.Cache.DefaultTtl, route.Cache.RespectOriginCacheControl, route.Cache.VaryByHeaders, route.Cache.CacheableStatusCodes, route.Cache.Methods));
    }

    private static Http1RequestHead Request(string method, string target, string host)
    {
        var path = target.Split('?', 2)[0];
        return new Http1RequestHead(method, target, path, "HTTP/1.1", host, Http1RequestFraming.None, [new ProxyHeaderField("Host", host)]);
    }

    private static Http1ResponseHead Response(string status, IReadOnlyList<ProxyHeaderField> headers)
    {
        var split = status.Split(' ', 2);
        return new Http1ResponseHead("HTTP/1.1", int.Parse(split[0], System.Globalization.CultureInfo.InvariantCulture), split.Length > 1 ? split[1] : "", Http1ResponseFraming.FromContentLength(0), headers);
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class MetricsFixture : IDisposable
    {
        private readonly UpstreamConnectionPool _pool;
        private MetricsFixture(ProxyMetrics metrics, ResponseCacheStore cache, UpstreamHealthStore health, AcmeCertificateStatusStore acme, PrometheusMetricsExporter exporter, UpstreamConnectionPool pool)
        {
            Metrics = metrics;
            Cache = cache;
            Health = health;
            Acme = acme;
            Exporter = exporter;
            _pool = pool;
        }

        public ProxyMetrics Metrics { get; }
        public ResponseCacheStore Cache { get; }
        public UpstreamHealthStore Health { get; }
        public AcmeCertificateStatusStore Acme { get; }
        public PrometheusMetricsExporter Exporter { get; }

        public string Export(ProxyConfigurationSnapshot snapshot)
        {
            return Exporter.Export(MetricsExportInput(snapshot, Metrics.Snapshot(), Cache.ReadStatusSnapshot(), Health.ReadUpstreams(ProxyUpstreamHealthSourceMapper.FromRoutes(snapshot.Routes)), Acme.Snapshot()));
        }

        private static ProxyMetricsExportInput MetricsExportInput(ProxyConfigurationSnapshot snapshot, ProxyMetricsSnapshot metrics, ProxyCacheRuntimeStatusSnapshot cacheRuntime, IReadOnlyList<ProxyUpstreamStatus> upstreamHealth, IReadOnlyList<AcmeCertificateLifecycleStatus> acmeCertificates)
        {
            return ProxyMetricsExportInputMapper.FromSources(metrics, ProxyMetricsExportLabelOptionsMapper.FromMetrics(snapshot.Metrics), ProxyMetricsExportHttp3FactsMapper.FromSources(snapshot.Listeners, snapshot.Routes), ProxyCacheStatusReader.Project(ProxyCacheStatusRouteSourceMapper.ToRouteSources(snapshot.Routes), cacheRuntime), upstreamHealth, acmeCertificates);
        }

        public static MetricsFixture Create()
        {
            var metrics = new ProxyMetrics();
            var cache = new ResponseCacheStore(TimeProvider.System);
            var pool = new UpstreamConnectionPool(new UpstreamConnectionFactory(), metrics, TimeProvider.System);
            var circuit = new CircuitBreakerStore(metrics, TimeProvider.System);
            var health = new UpstreamHealthStore(metrics, pool, circuit);
            var acme = new AcmeCertificateStatusStore();
            return new MetricsFixture(metrics, cache, health, acme, new PrometheusMetricsExporter(), pool);
        }

        public void Dispose()
        {
            _pool.Dispose();
        }
    }

    private sealed class FixedMetricsExportAvailabilityReader : IProxyMetricsExportAvailabilityReader
    {
        private readonly ProxyMetricsExportAvailabilityState _state;
        public FixedMetricsExportAvailabilityReader(bool hasActiveConfiguration, bool metricsExportEnabled)
        {
            _state = new ProxyMetricsExportAvailabilityState(hasActiveConfiguration, metricsExportEnabled);
        }

        public ProxyMetricsExportAvailabilityState Read()
        {
            return _state;
        }
    }

    private sealed class MissingMetricsExportConfigurationSource : IProxyMetricsExportConfigurationSource
    {
        public static MissingMetricsExportConfigurationSource Instance { get; } = new();

        public ProxyMetricsExportConfigurationReadResult ReadConfiguration()
        {
            return ProxyMetricsExportConfigurationReadResult.MissingConfiguration;
        }
    }

    private sealed class SinglePassEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _source;
        private bool _enumerated;
        public SinglePassEnumerable(IEnumerable<T> source)
        {
            ArgumentNullException.ThrowIfNull(source);
            _source = source;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "HLQ006", Justification = "IEnumerable<T> fixture forwards the original IEnumerator<T>; shared instance state detects repeated enumeration.") ]
        public IEnumerator<T> GetEnumerator()
        {
            AssertEx.False(_enumerated, "Metrics HTTP/3 source facts should be enumerated once.");
            _enumerated = true;
            return _source.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdrava-metrics-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
