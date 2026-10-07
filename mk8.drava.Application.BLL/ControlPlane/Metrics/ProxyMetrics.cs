using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.Status;
using System.Collections.Concurrent;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed partial class ProxyMetrics : IProxyStatusMetricsSource, IProxyUpstreamSelectionMetricsSink, IProxyCircuitBreakerMetricsSink, IProxyUpstreamHealthMetricsSink, IProxyHttp3AltSvcMetricsSink, IProxyAccessLogMetricsSink, IProxyRequestDiagnosticsMetricsSink, IProxyRequestIdMetricsSink, IProxyRateLimitMetricsSink, IProxyAdmissionMetricsSink, IProxyConfigurationReloadMetricsSink, IProxyHealthCheckMetricsSink, IProxyAcmeMetricsSink, IProxyAdminAuthenticationMetricsSink, IProxyConfigLintMetricsSink, IProxyRouteDiagnosticsMetricsSink
{
    public void ConfigReloadSucceeded() => Interlocked.Increment(ref _configReloadSuccesses);
    public void ConfigReloadFailed() => Interlocked.Increment(ref _configReloadFailures);
    public void AdminAuthSucceeded() => Interlocked.Increment(ref _adminAuthSuccesses);
    public void AdminAuthFailed() => Interlocked.Increment(ref _adminAuthFailures);
    public void AcmeRenewalAttempted() => Interlocked.Increment(ref _acmeRenewalAttempts);
    public void AcmeRenewalSucceeded() => Interlocked.Increment(ref _acmeRenewalSuccesses);
    public void AcmeRenewalFailed() => Interlocked.Increment(ref _acmeRenewalFailures);
    public void RetryAttempted() => Interlocked.Increment(ref _retryAttempts);
    public void RetryExhausted() => Interlocked.Increment(ref _retryExhausted);
    public void RetrySkipped(string reason)
    {
        var counter = _retrySkippedByReason.GetOrAdd(ProxyMetricLabelPolicy.NormalizeValue(reason), static _ => new RequestSeriesCounter());
        Interlocked.Increment(ref counter.Count);
    }

    public void CircuitOpened() => Interlocked.Increment(ref _circuitOpened);
    public void CircuitHalfOpened() => Interlocked.Increment(ref _circuitHalfOpened);
    public void CircuitClosed() => Interlocked.Increment(ref _circuitClosed);
    public void CircuitRejected() => Interlocked.Increment(ref _circuitRejections);
    public void ListenerReloadAttempted() => Interlocked.Increment(ref _listenerReloadAttempts);
    public void ListenerReloadSucceeded(int added, int removed, int changed, int unchanged)
    {
        Interlocked.Increment(ref _listenerReloadSuccesses);
        Interlocked.Add(ref _listenerReloadAdded, added);
        Interlocked.Add(ref _listenerReloadRemoved, removed);
        Interlocked.Add(ref _listenerReloadChanged, changed);
        Interlocked.Add(ref _listenerReloadUnchanged, unchanged);
    }

    public void ListenerReloadFailed() => Interlocked.Increment(ref _listenerReloadFailures);
    public void ListenerStartFailed() => Interlocked.Increment(ref _listenerStartFailures);
    public void ListenerDrained() => Interlocked.Increment(ref _listenerDrainCount);
    public void SetActiveListeners(long count) => Interlocked.Exchange(ref _activeListeners, count);
    public void ConfigLintRun(IReadOnlyList<ConfigLintFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        Interlocked.Increment(ref _configLintRuns);
        foreach (var finding in findings)
        {
            var key = new ConfigLintFindingKey(ProxyMetricLabelPolicy.NormalizeValue(finding.Severity), ProxyMetricLabelPolicy.NormalizeValue(finding.Code));
            var counter = _configLintFindings.GetOrAdd(key, static _ => new RequestSeriesCounter());
            Interlocked.Increment(ref counter.Count);
        }
    }

    public void RouteMatchDryRun(string? failureReason)
    {
        Interlocked.Increment(ref _routeMatchDryRuns);
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            return;
        }

        var counter = _routeMatchDryRunFailures.GetOrAdd(ProxyMetricLabelPolicy.NormalizeValue(failureReason), static _ => new RequestSeriesCounter());
        Interlocked.Increment(ref counter.Count);
    }

    public void UpgradeRequestReceived() => Interlocked.Increment(ref _upgradeRequestsReceived);
    public void UpgradeRequestSucceeded() => Interlocked.Increment(ref _upgradeRequestsSucceeded);
    public void UpgradeRequestRejected() => Interlocked.Increment(ref _upgradeRequestsRejected);
    public void UpgradeUpstreamFailed() => Interlocked.Increment(ref _upgradeUpstreamFailures);
    public void UpstreamSelected(ProxyUpstreamSelectionMetric selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Interlocked.Increment(ref _upstreamSelections);
        var key = new UpstreamSelectionKey(ProxyMetricLabelPolicy.NormalizeValue(selection.Route), ProxyMetricLabelPolicy.NormalizeValue(selection.Upstream), ProxyMetricLabelPolicy.NormalizeValue(selection.Scheme), ProxyMetricLabelPolicy.NormalizeValue(selection.Protocol));
        var counter = _upstreamSelectionsByUpstream.GetOrAdd(key, static _ => new RequestSeriesCounter());
        Interlocked.Increment(ref counter.Count);
    }

    public void NoHealthyUpstream() => Interlocked.Increment(ref _noHealthyUpstreamFailures);
    public void NoAvailableUpstream() => Interlocked.Increment(ref _noAvailableUpstreamFailures);
    public void UpstreamConnectionOpened() => Interlocked.Increment(ref _upstreamConnectionsOpened);
    public void UpstreamConnectionReused() => Interlocked.Increment(ref _upstreamConnectionsReused);
    public void UpstreamConnectionDiscarded() => Interlocked.Increment(ref _upstreamConnectionsDiscarded);
    public ProxyMetricsSnapshot Snapshot()
    {
        return new ProxyMetricsSnapshot(ReadClientConnectionMetricsSnapshot(), ReadTrafficMetricsSnapshot(), ReadUpstreamForwardingMetricsSnapshot(), ReadClientFailureMetricsSnapshot(), ReadRejectionMetricsSnapshot(), ReadUpstreamFailureMetricsSnapshot(), ReadGeneratedResponseMetricsSnapshot(), ReadTlsMetricsSnapshot(), ReadUpstreamPoolMetricsSnapshot(), ReadUpgradeMetricsSnapshot(), ReadTunnelMetricsSnapshot(), ReadUpstreamSelectionMetricsSnapshot(), ReadHealthMetricsSnapshot(), ReadDiagnosticsMetricsSnapshot(), ReadRequestClassificationMetricsSnapshot(), ReadConfigReloadMetricsSnapshot(), ReadAdminAuthMetricsSnapshot(), ReadAcmeRenewalMetricsSnapshot(), ReadResilienceMetricsSnapshot(), ReadListenerMetricsSnapshot(), ReadHttp2MetricsSnapshot(), ReadUpstreamHttp2MetricsSnapshot(), ReadUpstreamHttp3MetricsSnapshot(), ReadHttp3MetricsSnapshot(), ReadConfigLintMetricsSnapshot(), ReadRouteDiagnosticsMetricsSnapshot());
    }

    private ProxyTrafficMetricsSnapshot ReadTrafficMetricsSnapshot()
    {
        return new ProxyTrafficMetricsSnapshot(Interlocked.Read(ref _totalRequests), Interlocked.Read(ref _bytesRead), Interlocked.Read(ref _bytesWritten));
    }

    private ProxyClientFailureMetricsSnapshot ReadClientFailureMetricsSnapshot()
    {
        return new ProxyClientFailureMetricsSnapshot(Interlocked.Read(ref _parseErrors), Interlocked.Read(ref _clientBodyRelayFailures), Interlocked.Read(ref _clientRequestHeadTimeouts), Interlocked.Read(ref _clientRequestBodyTimeouts), Interlocked.Read(ref _clientPrematureDisconnects), Interlocked.Read(ref _downstreamWriteTimeouts));
    }

    private ProxyUpstreamForwardingMetricsSnapshot ReadUpstreamForwardingMetricsSnapshot()
    {
        return new ProxyUpstreamForwardingMetricsSnapshot(Interlocked.Read(ref _upstreamSuccesses), Interlocked.Read(ref _upstreamFailures), Interlocked.Read(ref _upstreamBodyRelayFailures));
    }

    private ProxyUpstreamSelectionMetricsSnapshot ReadUpstreamSelectionMetricsSnapshot()
    {
        return new ProxyUpstreamSelectionMetricsSnapshot(Interlocked.Read(ref _upstreamSelections), ReadUpstreamSelectionsByUpstream());
    }

    private ProxyConfigLintMetricsSnapshot ReadConfigLintMetricsSnapshot()
    {
        return new ProxyConfigLintMetricsSnapshot(Interlocked.Read(ref _configLintRuns), ReadConfigLintFindings());
    }

    private ProxyRouteDiagnosticsMetricsSnapshot ReadRouteDiagnosticsMetricsSnapshot()
    {
        return new ProxyRouteDiagnosticsMetricsSnapshot(Interlocked.Read(ref _routeMatchDryRuns), ReadRouteMatchDryRunFailures());
    }

    private ProxyRequestClassificationMetricsSnapshot ReadRequestClassificationMetricsSnapshot()
    {
        return new ProxyRequestClassificationMetricsSnapshot(ReadRequestFailuresByKind(), ReadRequestsByRoute());
    }

    private ProxyClientConnectionMetricsSnapshot ReadClientConnectionMetricsSnapshot()
    {
        return new ProxyClientConnectionMetricsSnapshot(Interlocked.Read(ref _acceptedConnections), Interlocked.Read(ref _activeConnections), Interlocked.Read(ref _clientConnectionsClosedByIdleTimeout), Interlocked.Read(ref _clientConnectionsClosedByMaxRequests));
    }

    private ProxyUpstreamFailureMetricsSnapshot ReadUpstreamFailureMetricsSnapshot()
    {
        return new ProxyUpstreamFailureMetricsSnapshot(Interlocked.Read(ref _upstreamConnectFailures), Interlocked.Read(ref _upstreamConnectTimeouts), Interlocked.Read(ref _upstreamResponseHeadTimeouts), Interlocked.Read(ref _upstreamResponseBodyTimeouts), Interlocked.Read(ref _upstreamMalformedResponses), Interlocked.Read(ref _upstreamPrematureDisconnects), Interlocked.Read(ref _upstreamRequestFailures));
    }

    private ProxyGeneratedResponseMetricsSnapshot ReadGeneratedResponseMetricsSnapshot()
    {
        return new ProxyGeneratedResponseMetricsSnapshot(Interlocked.Read(ref _proxyGenerated502Responses), Interlocked.Read(ref _proxyGenerated504Responses));
    }

    private ProxyHttp3MetricsSnapshot ReadHttp3MetricsSnapshot()
    {
        return new ProxyHttp3MetricsSnapshot(Interlocked.Read(ref _http3AcceptedConnections), Interlocked.Read(ref _activeHttp3Connections), Interlocked.Read(ref _http3Requests), Interlocked.Read(ref _http3ProxiedRequests), Interlocked.Read(ref _http3GeneratedResponses), Interlocked.Read(ref _activeHttp3Streams), Interlocked.Read(ref _http3StreamResets), Interlocked.Read(ref _http3StreamedResponses), Interlocked.Read(ref _activeHttp3ResponseStreams), Interlocked.Read(ref _http3ResponseBytesSent), Interlocked.Read(ref _http3RequestBodyBytesReceived), Interlocked.Read(ref _http3ResponseStreamResets), Interlocked.Read(ref _http3AltSvcEmitted), Interlocked.Read(ref _http3AltSvcSuppressed), ReadHttp3RequestsByOutcome(), ReadHttp3RejectedRequests(), ReadHttp3ProtocolErrors(), Interlocked.Read(ref _quicListenerStartSuccesses), Interlocked.Read(ref _quicListenerStartFailures), Interlocked.Read(ref _activeQuicListeners));
    }

    private ProxyListenerMetricsSnapshot ReadListenerMetricsSnapshot()
    {
        return new ProxyListenerMetricsSnapshot(Interlocked.Read(ref _listenerReloadAttempts), Interlocked.Read(ref _listenerReloadSuccesses), Interlocked.Read(ref _listenerReloadFailures), Interlocked.Read(ref _listenerReloadAdded), Interlocked.Read(ref _listenerReloadRemoved), Interlocked.Read(ref _listenerReloadChanged), Interlocked.Read(ref _listenerReloadUnchanged), Interlocked.Read(ref _listenerStartFailures), Interlocked.Read(ref _listenerDrainCount), Interlocked.Read(ref _activeListeners));
    }

    private ProxyTunnelMetricsSnapshot ReadTunnelMetricsSnapshot()
    {
        return new ProxyTunnelMetricsSnapshot(Interlocked.Read(ref _activeTunnels), Interlocked.Read(ref _totalTunnels), Interlocked.Read(ref _tunnelIdleTimeouts), Interlocked.Read(ref _tunnelBytesClientToUpstream), Interlocked.Read(ref _tunnelBytesUpstreamToClient), Interlocked.Read(ref _tunnelRelayFailures));
    }

    private ProxyTlsMetricsSnapshot ReadTlsMetricsSnapshot()
    {
        return new ProxyTlsMetricsSnapshot(Interlocked.Read(ref _tlsHandshakeAttempts), Interlocked.Read(ref _tlsHandshakeSuccesses), Interlocked.Read(ref _tlsHandshakeFailures), Interlocked.Read(ref _tlsHandshakeTimeouts), Interlocked.Read(ref _tlsNoCertificateForSniFailures), Interlocked.Read(ref _activeTlsHandshakes), Interlocked.Read(ref _tlsHandshakeAdmissionRejections));
    }

    private ProxyUpstreamPoolMetricsSnapshot ReadUpstreamPoolMetricsSnapshot()
    {
        return new ProxyUpstreamPoolMetricsSnapshot(Interlocked.Read(ref _upstreamConnectionsOpened), Interlocked.Read(ref _upstreamConnectionsReused), Interlocked.Read(ref _upstreamConnectionsDiscarded), Interlocked.Read(ref _upstreamPoolIdleConnections), Interlocked.Read(ref _upstreamPoolActiveConnections));
    }

    private ProxyUpgradeMetricsSnapshot ReadUpgradeMetricsSnapshot()
    {
        return new ProxyUpgradeMetricsSnapshot(Interlocked.Read(ref _upgradeRequestsReceived), Interlocked.Read(ref _upgradeRequestsSucceeded), Interlocked.Read(ref _upgradeRequestsRejected), Interlocked.Read(ref _upgradeUpstreamFailures));
    }

    private ProxyHealthMetricsSnapshot ReadHealthMetricsSnapshot()
    {
        return new ProxyHealthMetricsSnapshot(Interlocked.Read(ref _noHealthyUpstreamFailures), Interlocked.Read(ref _healthChecksAttempted), Interlocked.Read(ref _healthChecksSucceeded), Interlocked.Read(ref _healthChecksFailed), Interlocked.Read(ref _upstreamHealthTransitions));
    }

    private ProxyRejectionMetricsSnapshot ReadRejectionMetricsSnapshot()
    {
        return new ProxyRejectionMetricsSnapshot(Interlocked.Read(ref _connectionAdmissionRejections), Interlocked.Read(ref _rateLimitedRequests), Interlocked.Read(ref _rateLimitedUpgrades), Interlocked.Read(ref _requestBodySizeRejections), Interlocked.Read(ref _parserLimitRejections), Interlocked.Read(ref _rejectedMalformedRequests), Interlocked.Read(ref _rejectedUnsupportedRequestFraming));
    }

    private ProxyDiagnosticsMetricsSnapshot ReadDiagnosticsMetricsSnapshot()
    {
        return new ProxyDiagnosticsMetricsSnapshot(Interlocked.Read(ref _requestIdsGenerated), Interlocked.Read(ref _accessLogsEmitted), Interlocked.Read(ref _recentDiagnosticsOverwritten));
    }

    private ProxyConfigReloadMetricsSnapshot ReadConfigReloadMetricsSnapshot()
    {
        return new ProxyConfigReloadMetricsSnapshot(Interlocked.Read(ref _configReloadSuccesses), Interlocked.Read(ref _configReloadFailures));
    }

    private ProxyAdminAuthMetricsSnapshot ReadAdminAuthMetricsSnapshot()
    {
        return new ProxyAdminAuthMetricsSnapshot(Interlocked.Read(ref _adminAuthSuccesses), Interlocked.Read(ref _adminAuthFailures));
    }

    private ProxyAcmeRenewalMetricsSnapshot ReadAcmeRenewalMetricsSnapshot()
    {
        return new ProxyAcmeRenewalMetricsSnapshot(Interlocked.Read(ref _acmeRenewalAttempts), Interlocked.Read(ref _acmeRenewalSuccesses), Interlocked.Read(ref _acmeRenewalFailures));
    }

    private ProxyResilienceMetricsSnapshot ReadResilienceMetricsSnapshot()
    {
        return new ProxyResilienceMetricsSnapshot(Interlocked.Read(ref _retryAttempts), Interlocked.Read(ref _retryExhausted), ReadRetrySkipped(), Interlocked.Read(ref _circuitOpened), Interlocked.Read(ref _circuitHalfOpened), Interlocked.Read(ref _circuitClosed), Interlocked.Read(ref _circuitRejections), Interlocked.Read(ref _noAvailableUpstreamFailures));
    }

    private ProxyUpstreamHttp3MetricsSnapshot ReadUpstreamHttp3MetricsSnapshot()
    {
        return new ProxyUpstreamHttp3MetricsSnapshot(Interlocked.Read(ref _upstreamHttp3Requests), Interlocked.Read(ref _upstreamHttp3ConnectionAttempts), Interlocked.Read(ref _upstreamHttp3ConnectionSuccesses), Interlocked.Read(ref _upstreamHttp3ConnectionFailures), Interlocked.Read(ref _upstreamHttp3PoolConnectionsOpened), Interlocked.Read(ref _upstreamHttp3PoolConnectionsReused), Interlocked.Read(ref _upstreamHttp3PoolConnectionsClosed), Interlocked.Read(ref _upstreamHttp3StreamLimitRejections), Interlocked.Read(ref _activeUpstreamHttp3Connections), Interlocked.Read(ref _activeUpstreamHttp3Streams), ReadUpstreamHttp3ProtocolErrors());
    }

    private ProxyHttp2MetricsSnapshot ReadHttp2MetricsSnapshot()
    {
        return new ProxyHttp2MetricsSnapshot(Interlocked.Read(ref _http2AcceptedConnections), Interlocked.Read(ref _http2Requests), Interlocked.Read(ref _activeHttp2Streams), ReadHttp2ProtocolErrors());
    }

    private ProxyUpstreamHttp2MetricsSnapshot ReadUpstreamHttp2MetricsSnapshot()
    {
        return new ProxyUpstreamHttp2MetricsSnapshot(Interlocked.Read(ref _upstreamHttp2Requests), Interlocked.Read(ref _upstreamHttp2AlpnFailures), Interlocked.Read(ref _upstreamHttp2ProtocolErrors));
    }

    public ProxyMetricsSnapshot ReadMetrics()
    {
        return Snapshot();
    }

    private static readonly ProxyFailureKind[] FailureKinds = Enum.GetValues<ProxyFailureKind>();
    private long _acceptedConnections;
    private long _activeConnections;
    private long _totalRequests;
    private long _upstreamSuccesses;
    private long _upstreamFailures;
    private long _bytesRead;
    private long _bytesWritten;
    private long _parseErrors;
    private long _rejectedMalformedRequests;
    private long _rejectedUnsupportedRequestFraming;
    private long _upstreamMalformedResponses;
    private long _clientBodyRelayFailures;
    private long _upstreamBodyRelayFailures;
    private long _clientRequestHeadTimeouts;
    private long _clientRequestBodyTimeouts;
    private long _upstreamConnectFailures;
    private long _upstreamConnectTimeouts;
    private long _upstreamResponseHeadTimeouts;
    private long _upstreamResponseBodyTimeouts;
    private long _upstreamPrematureDisconnects;
    private long _clientPrematureDisconnects;
    private long _proxyGenerated502Responses;
    private long _proxyGenerated504Responses;
    private long _downstreamWriteTimeouts;
    private long _tlsHandshakeAttempts;
    private long _tlsHandshakeSuccesses;
    private long _tlsHandshakeFailures;
    private long _tlsHandshakeTimeouts;
    private long _tlsNoCertificateForSniFailures;
    private long _clientConnectionsClosedByIdleTimeout;
    private long _clientConnectionsClosedByMaxRequests;
    private long _upstreamConnectionsOpened;
    private long _upstreamConnectionsReused;
    private long _upstreamConnectionsDiscarded;
    private long _upstreamPoolIdleConnections;
    private long _upstreamPoolActiveConnections;
    private long _upgradeRequestsReceived;
    private long _upgradeRequestsSucceeded;
    private long _upgradeRequestsRejected;
    private long _upgradeUpstreamFailures;
    private long _activeTunnels;
    private long _totalTunnels;
    private long _tunnelIdleTimeouts;
    private long _tunnelBytesClientToUpstream;
    private long _tunnelBytesUpstreamToClient;
    private long _tunnelRelayFailures;
    private long _upstreamSelections;
    private long _noHealthyUpstreamFailures;
    private long _healthChecksAttempted;
    private long _healthChecksSucceeded;
    private long _healthChecksFailed;
    private long _upstreamHealthTransitions;
    private long _upstreamRequestFailures;
    private long _requestIdsGenerated;
    private long _accessLogsEmitted;
    private long _recentDiagnosticsOverwritten;
    private long _connectionAdmissionRejections;
    private long _activeTlsHandshakes;
    private long _tlsHandshakeAdmissionRejections;
    private long _rateLimitedRequests;
    private long _rateLimitedUpgrades;
    private long _requestBodySizeRejections;
    private long _parserLimitRejections;
    private long _configReloadSuccesses;
    private long _configReloadFailures;
    private long _adminAuthSuccesses;
    private long _adminAuthFailures;
    private long _acmeRenewalAttempts;
    private long _acmeRenewalSuccesses;
    private long _acmeRenewalFailures;
    private long _retryAttempts;
    private long _retryExhausted;
    private long _circuitOpened;
    private long _circuitHalfOpened;
    private long _circuitClosed;
    private long _circuitRejections;
    private long _noAvailableUpstreamFailures;
    private long _listenerReloadAttempts;
    private long _listenerReloadSuccesses;
    private long _listenerReloadFailures;
    private long _listenerReloadAdded;
    private long _listenerReloadRemoved;
    private long _listenerReloadChanged;
    private long _listenerReloadUnchanged;
    private long _listenerStartFailures;
    private long _listenerDrainCount;
    private long _activeListeners;
    private long _http2AcceptedConnections;
    private long _http2Requests;
    private long _activeHttp2Streams;
    private long _upstreamHttp2Requests;
    private long _upstreamHttp2AlpnFailures;
    private long _upstreamHttp2ProtocolErrors;
    private long _upstreamHttp3Requests;
    private long _upstreamHttp3ConnectionAttempts;
    private long _upstreamHttp3ConnectionSuccesses;
    private long _upstreamHttp3ConnectionFailures;
    private long _upstreamHttp3PoolConnectionsOpened;
    private long _upstreamHttp3PoolConnectionsReused;
    private long _upstreamHttp3PoolConnectionsClosed;
    private long _upstreamHttp3StreamLimitRejections;
    private long _activeUpstreamHttp3Connections;
    private long _activeUpstreamHttp3Streams;
    private long _http3AcceptedConnections;
    private long _activeHttp3Connections;
    private long _http3Requests;
    private long _http3ProxiedRequests;
    private long _http3GeneratedResponses;
    private long _activeHttp3Streams;
    private long _http3StreamResets;
    private long _http3StreamedResponses;
    private long _activeHttp3ResponseStreams;
    private long _http3ResponseBytesSent;
    private long _http3RequestBodyBytesReceived;
    private long _http3ResponseStreamResets;
    private long _http3AltSvcEmitted;
    private long _http3AltSvcSuppressed;
    private long _quicListenerStartSuccesses;
    private long _quicListenerStartFailures;
    private long _activeQuicListeners;
    private long _configLintRuns;
    private long _routeMatchDryRuns;
    private readonly long[] _requestFailuresByKind = new long[FailureKinds.Length];
    private readonly ConcurrentDictionary<RequestSeriesKey, RequestSeriesCounter> _requestsByRoute = new();
    private readonly ConcurrentDictionary<string, RequestSeriesCounter> _retrySkippedByReason = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<UpstreamSelectionKey, RequestSeriesCounter> _upstreamSelectionsByUpstream = new();
    private readonly ConcurrentDictionary<string, RequestSeriesCounter> _http2ProtocolErrors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RequestSeriesCounter> _upstreamHttp3ProtocolErrors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Http3OutcomeKey, RequestSeriesCounter> _http3RequestsByOutcome = new();
    private readonly ConcurrentDictionary<string, RequestSeriesCounter> _http3RejectedRequests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RequestSeriesCounter> _http3ProtocolErrors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<ConfigLintFindingKey, RequestSeriesCounter> _configLintFindings = new();
    private readonly ConcurrentDictionary<string, RequestSeriesCounter> _routeMatchDryRunFailures = new(StringComparer.Ordinal);
    private readonly record struct RequestSeriesKey(string Site, string Route, string Action, string StatusClass);
    private readonly record struct UpstreamSelectionKey(string Route, string Upstream, string Scheme, string Protocol);
    private readonly record struct Http3OutcomeKey(string Method, string Outcome, string StatusClass);
    private readonly record struct ConfigLintFindingKey(string Severity, string Code);
    private sealed class RequestSeriesCounter
    {
        public long Count;
    }

    private Dictionary<string, long> ReadRequestFailuresByKind()
    {
        Dictionary<string, long> failuresByKind = new(StringComparer.Ordinal);
        foreach (var failureKind in FailureKinds)
        {
            if (failureKind == ProxyFailureKind.None)
            {
                continue;
            }

            failuresByKind[ProxyFailureKindText.FromFailureKind(failureKind)] = Interlocked.Read(ref _requestFailuresByKind[(int)failureKind]);
        }

        return failuresByKind;
    }

    private IEnumerable<ProxyRequestSeriesSnapshot> ReadRequestsByRoute()
    {
        return _requestsByRoute.Select(static pair => new ProxyRequestSeriesSnapshot(pair.Key.Site, pair.Key.Route, pair.Key.Action, pair.Key.StatusClass, Interlocked.Read(ref pair.Value.Count))).OrderBy(static item => item.Site, StringComparer.Ordinal).ThenBy(static item => item.Route, StringComparer.Ordinal).ThenBy(static item => item.Action, StringComparer.Ordinal).ThenBy(static item => item.StatusClass, StringComparer.Ordinal);
    }

    private IEnumerable<ProxyRetrySkippedSnapshot> ReadRetrySkipped()
    {
        return _retrySkippedByReason.Select(static pair => new ProxyRetrySkippedSnapshot(pair.Key, Interlocked.Read(ref pair.Value.Count))).OrderBy(static item => item.Reason, StringComparer.Ordinal);
    }

    private IEnumerable<ProxyUpstreamSelectionSnapshot> ReadUpstreamSelectionsByUpstream()
    {
        return _upstreamSelectionsByUpstream.Select(static pair => new ProxyUpstreamSelectionSnapshot(pair.Key.Route, pair.Key.Upstream, pair.Key.Scheme, pair.Key.Protocol, Interlocked.Read(ref pair.Value.Count))).OrderBy(static item => item.Route, StringComparer.Ordinal).ThenBy(static item => item.Upstream, StringComparer.Ordinal).ThenBy(static item => item.Scheme, StringComparer.Ordinal);
    }

    private Dictionary<string, long> ReadHttp2ProtocolErrors()
    {
        return _http2ProtocolErrors.ToDictionary(static pair => pair.Key, static pair => Interlocked.Read(ref pair.Value.Count), StringComparer.Ordinal);
    }

    private Dictionary<string, long> ReadUpstreamHttp3ProtocolErrors()
    {
        return _upstreamHttp3ProtocolErrors.ToDictionary(static pair => pair.Key, static pair => Interlocked.Read(ref pair.Value.Count), StringComparer.Ordinal);
    }

    private IEnumerable<ProxyHttp3RequestOutcomeSnapshot> ReadHttp3RequestsByOutcome()
    {
        return _http3RequestsByOutcome.Select(static pair => new ProxyHttp3RequestOutcomeSnapshot(pair.Key.Method, pair.Key.Outcome, pair.Key.StatusClass, Interlocked.Read(ref pair.Value.Count))).OrderBy(static item => item.Method, StringComparer.Ordinal).ThenBy(static item => item.Outcome, StringComparer.Ordinal).ThenBy(static item => item.StatusClass, StringComparer.Ordinal);
    }

    private Dictionary<string, long> ReadHttp3RejectedRequests()
    {
        return _http3RejectedRequests.ToDictionary(static pair => pair.Key, static pair => Interlocked.Read(ref pair.Value.Count), StringComparer.Ordinal);
    }

    private Dictionary<string, long> ReadHttp3ProtocolErrors()
    {
        return _http3ProtocolErrors.ToDictionary(static pair => pair.Key, static pair => Interlocked.Read(ref pair.Value.Count), StringComparer.Ordinal);
    }

    private IEnumerable<ProxyConfigLintFindingMetricSnapshot> ReadConfigLintFindings()
    {
        return _configLintFindings.Select(static pair => new ProxyConfigLintFindingMetricSnapshot(pair.Key.Severity, pair.Key.Code, Interlocked.Read(ref pair.Value.Count))).OrderBy(static item => item.Severity, StringComparer.Ordinal).ThenBy(static item => item.Code, StringComparer.Ordinal);
    }

    private IEnumerable<ProxyRouteDryRunFailureSnapshot> ReadRouteMatchDryRunFailures()
    {
        return _routeMatchDryRunFailures.Select(static pair => new ProxyRouteDryRunFailureSnapshot(pair.Key, Interlocked.Read(ref pair.Value.Count))).OrderBy(static item => item.Reason, StringComparer.Ordinal);
    }

    public void ClientRequestHeadTimedOut() => Interlocked.Increment(ref _clientRequestHeadTimeouts);
    public void ClientRequestBodyTimedOut() => Interlocked.Increment(ref _clientRequestBodyTimeouts);
    public void UpstreamConnectTimedOut() => Interlocked.Increment(ref _upstreamConnectTimeouts);
    public void UpstreamResponseHeadTimedOut() => Interlocked.Increment(ref _upstreamResponseHeadTimeouts);
    public void UpstreamResponseBodyTimedOut() => Interlocked.Increment(ref _upstreamResponseBodyTimeouts);
    public void DownstreamWriteTimedOut() => Interlocked.Increment(ref _downstreamWriteTimeouts);
    public void ParseFailed()
    {
        Interlocked.Increment(ref _parseErrors);
    }

    public void MalformedRequestRejected()
    {
        Interlocked.Increment(ref _rejectedMalformedRequests);
    }

    public void UnsupportedRequestFramingRejected()
    {
        Interlocked.Increment(ref _rejectedUnsupportedRequestFraming);
    }

    public void RequestFailed(ProxyFailureKind failureKind)
    {
        if (failureKind == ProxyFailureKind.None)
        {
            return;
        }

        var index = (int)failureKind;
        if ((uint)index < (uint)_requestFailuresByKind.Length)
        {
            Interlocked.Increment(ref _requestFailuresByKind[index]);
        }
    }

    public void UpstreamSucceeded()
    {
        Interlocked.Increment(ref _upstreamSuccesses);
    }

    public void UpstreamFailed()
    {
        Interlocked.Increment(ref _upstreamFailures);
    }

    public void UpstreamConnectFailed()
    {
        Interlocked.Increment(ref _upstreamConnectFailures);
    }

    public void UpstreamMalformedResponse()
    {
        Interlocked.Increment(ref _upstreamMalformedResponses);
    }

    public void ClientBodyRelayFailed()
    {
        Interlocked.Increment(ref _clientBodyRelayFailures);
    }

    public void UpstreamBodyRelayFailed()
    {
        Interlocked.Increment(ref _upstreamBodyRelayFailures);
    }

    public void UpstreamPrematureDisconnect() => Interlocked.Increment(ref _upstreamPrematureDisconnects);
    public void GeneratedFailureResponse(int statusCode)
    {
        if (statusCode == 502)
        {
            ProxyGenerated502();
        }
        else if (statusCode == 504)
        {
            ProxyGenerated504();
        }
    }

    private void ProxyGenerated502() => Interlocked.Increment(ref _proxyGenerated502Responses);
    private void ProxyGenerated504() => Interlocked.Increment(ref _proxyGenerated504Responses);
    public void UpstreamRequestFailed() => Interlocked.Increment(ref _upstreamRequestFailures);
    public void Http2ConnectionAccepted() => Interlocked.Increment(ref _http2AcceptedConnections);
    public void Http2RequestReceived() => Interlocked.Increment(ref _http2Requests);
    public void Http2StreamStarted() => Interlocked.Increment(ref _activeHttp2Streams);
    public void Http2StreamEnded() => Interlocked.Decrement(ref _activeHttp2Streams);
    public void Http2ProtocolError(string reason)
    {
        var counter = _http2ProtocolErrors.GetOrAdd(ProxyMetricLabelPolicy.NormalizeValue(reason), static _ => new RequestSeriesCounter());
        Interlocked.Increment(ref counter.Count);
    }

    public void UpstreamHttp2RequestAttempted() => Interlocked.Increment(ref _upstreamHttp2Requests);
    public void UpstreamHttp2AlpnFailed() => Interlocked.Increment(ref _upstreamHttp2AlpnFailures);
    public void UpstreamHttp2ProtocolError() => Interlocked.Increment(ref _upstreamHttp2ProtocolErrors);
    public void UpstreamHttp3RequestAttempted() => Interlocked.Increment(ref _upstreamHttp3Requests);
    public void UpstreamHttp3ConnectionAttempted() => Interlocked.Increment(ref _upstreamHttp3ConnectionAttempts);
    public void UpstreamHttp3ConnectionSucceeded() => Interlocked.Increment(ref _upstreamHttp3ConnectionSuccesses);
    public void UpstreamHttp3ConnectionFailed() => Interlocked.Increment(ref _upstreamHttp3ConnectionFailures);
    public void UpstreamHttp3PoolConnectionOpened() => Interlocked.Increment(ref _upstreamHttp3PoolConnectionsOpened);
    public void UpstreamHttp3PoolConnectionReused() => Interlocked.Increment(ref _upstreamHttp3PoolConnectionsReused);
    public void UpstreamHttp3PoolConnectionClosed() => Interlocked.Increment(ref _upstreamHttp3PoolConnectionsClosed);
    public void UpstreamHttp3StreamLimitRejected() => Interlocked.Increment(ref _upstreamHttp3StreamLimitRejections);
    public void UpstreamHttp3ConnectionOpened() => Interlocked.Increment(ref _activeUpstreamHttp3Connections);
    public void UpstreamHttp3ConnectionClosed() => Interlocked.Decrement(ref _activeUpstreamHttp3Connections);
    public void UpstreamHttp3StreamStarted() => Interlocked.Increment(ref _activeUpstreamHttp3Streams);
    public void UpstreamHttp3StreamEnded() => Interlocked.Decrement(ref _activeUpstreamHttp3Streams);
    public void UpstreamHttp3ProtocolError(string reason)
    {
        var counter = _upstreamHttp3ProtocolErrors.GetOrAdd(ProxyMetricLabelPolicy.NormalizeValue(reason), static _ => new RequestSeriesCounter());
        Interlocked.Increment(ref counter.Count);
    }

    public void Http3ConnectionAccepted()
    {
        Interlocked.Increment(ref _http3AcceptedConnections);
        Interlocked.Increment(ref _activeHttp3Connections);
    }

    public void Http3ConnectionClosed() => Interlocked.Decrement(ref _activeHttp3Connections);
    public void Http3RequestReceived() => Interlocked.Increment(ref _http3Requests);
    public void Http3RequestCompleted(string? method, int? statusCode, string? outcome)
    {
        var key = new Http3OutcomeKey(ProxyMetricLabelPolicy.NormalizeValue(method), ProxyMetricLabelPolicy.NormalizeValue(outcome), ProxyMetricLabelPolicy.StatusClass(statusCode));
        var counter = _http3RequestsByOutcome.GetOrAdd(key, static _ => new RequestSeriesCounter());
        Interlocked.Increment(ref counter.Count);
    }

    public void Http3ProxiedRequest() => Interlocked.Increment(ref _http3ProxiedRequests);
    public void Http3GeneratedResponse() => Interlocked.Increment(ref _http3GeneratedResponses);
    public void Http3StreamStarted() => Interlocked.Increment(ref _activeHttp3Streams);
    public void Http3StreamEnded() => Interlocked.Decrement(ref _activeHttp3Streams);
    public void Http3StreamReset() => Interlocked.Increment(ref _http3StreamResets);
    public void Http3StreamedResponse() => Interlocked.Increment(ref _http3StreamedResponses);
    public void Http3ResponseStreamStarted() => Interlocked.Increment(ref _activeHttp3ResponseStreams);
    public void Http3ResponseStreamEnded() => Interlocked.Decrement(ref _activeHttp3ResponseStreams);
    public void AddHttp3ResponseBytesSent(long bytes)
    {
        if (bytes > 0)
        {
            Interlocked.Add(ref _http3ResponseBytesSent, bytes);
        }
    }

    public void AddHttp3RequestBodyBytesReceived(long bytes)
    {
        if (bytes > 0)
        {
            Interlocked.Add(ref _http3RequestBodyBytesReceived, bytes);
        }
    }

    public void Http3ResponseStreamReset() => Interlocked.Increment(ref _http3ResponseStreamResets);
    public void Http3AltSvcEmitted() => Interlocked.Increment(ref _http3AltSvcEmitted);
    public void Http3AltSvcSuppressed() => Interlocked.Increment(ref _http3AltSvcSuppressed);
    public void Http3RequestRejected(string reason)
    {
        var counter = _http3RejectedRequests.GetOrAdd(ProxyMetricLabelPolicy.NormalizeValue(reason), static _ => new RequestSeriesCounter());
        Interlocked.Increment(ref counter.Count);
    }

    public void Http3ProtocolError(string reason)
    {
        var counter = _http3ProtocolErrors.GetOrAdd(ProxyMetricLabelPolicy.NormalizeValue(reason), static _ => new RequestSeriesCounter());
        Interlocked.Increment(ref counter.Count);
    }

    public void QuicListenerStarted() => Interlocked.Increment(ref _quicListenerStartSuccesses);
    public void QuicListenerStartFailed() => Interlocked.Increment(ref _quicListenerStartFailures);
    public void SetActiveQuicListeners(long count) => Interlocked.Exchange(ref _activeQuicListeners, count);
    public void RequestIdGenerated() => Interlocked.Increment(ref _requestIdsGenerated);
    public void AccessLogEmitted() => Interlocked.Increment(ref _accessLogsEmitted);
    public void RecentDiagnosticOverwritten() => Interlocked.Increment(ref _recentDiagnosticsOverwritten);
    public ProxyTunnelAdmissionDecision StartTunnel(int maxActiveTunnels)
    {
        while (true)
        {
            var observed = Interlocked.Read(ref _activeTunnels);
            if (observed >= maxActiveTunnels)
            {
                return ProxyTunnelAdmissionDecision.Rejected;
            }

            if (Interlocked.CompareExchange(ref _activeTunnels, observed + 1, observed) == observed)
            {
                return ProxyTunnelAdmissionDecision.Accepted;
            }
        }
    }

    public void TunnelStarted() => Interlocked.Increment(ref _totalTunnels);
    public void TunnelClosed() => Interlocked.Decrement(ref _activeTunnels);
    public void TunnelIdleTimedOut() => Interlocked.Increment(ref _tunnelIdleTimeouts);
    public void AddTunnelBytesClientToUpstream(long bytes)
    {
        if (bytes > 0)
        {
            Interlocked.Add(ref _tunnelBytesClientToUpstream, bytes);
        }
    }

    public void AddTunnelBytesUpstreamToClient(long bytes)
    {
        if (bytes > 0)
        {
            Interlocked.Add(ref _tunnelBytesUpstreamToClient, bytes);
        }
    }

    public void TunnelRelayFailed() => Interlocked.Increment(ref _tunnelRelayFailures);
    public void UpstreamPoolConnectionBorrowed()
    {
        Interlocked.Increment(ref _upstreamPoolActiveConnections);
    }

    public void UpstreamPoolConnectionReturnedIdle()
    {
        Interlocked.Decrement(ref _upstreamPoolActiveConnections);
        Interlocked.Increment(ref _upstreamPoolIdleConnections);
    }

    public void UpstreamPoolConnectionReusedFromIdle()
    {
        Interlocked.Decrement(ref _upstreamPoolIdleConnections);
        Interlocked.Increment(ref _upstreamPoolActiveConnections);
    }

    public void UpstreamPoolConnectionClosedActive()
    {
        Interlocked.Decrement(ref _upstreamPoolActiveConnections);
    }

    public void UpstreamPoolIdleConnectionDiscarded()
    {
        Interlocked.Decrement(ref _upstreamPoolIdleConnections);
    }

    public void ConnectionAdmissionRejected() => Interlocked.Increment(ref _connectionAdmissionRejections);
    public void TlsHandshakeStarted() => Interlocked.Increment(ref _activeTlsHandshakes);
    public void TlsHandshakeEnded() => Interlocked.Decrement(ref _activeTlsHandshakes);
    public void TlsHandshakeAdmissionRejected() => Interlocked.Increment(ref _tlsHandshakeAdmissionRejections);
    public void RequestRateLimited() => Interlocked.Increment(ref _rateLimitedRequests);
    public void UpgradeRateLimited() => Interlocked.Increment(ref _rateLimitedUpgrades);
    public void RequestBodySizeRejected() => Interlocked.Increment(ref _requestBodySizeRejections);
    public void ParserLimitRejected() => Interlocked.Increment(ref _parserLimitRejections);
    public void AddBytesRead(long bytes)
    {
        if (bytes > 0)
        {
            Interlocked.Add(ref _bytesRead, bytes);
        }
    }

    public void AddBytesWritten(long bytes)
    {
        if (bytes > 0)
        {
            Interlocked.Add(ref _bytesWritten, bytes);
        }
    }

    public void HealthCheckAttempted() => Interlocked.Increment(ref _healthChecksAttempted);
    public void HealthCheckSucceeded() => Interlocked.Increment(ref _healthChecksSucceeded);
    public void HealthCheckFailed() => Interlocked.Increment(ref _healthChecksFailed);
    public void UpstreamHealthTransition() => Interlocked.Increment(ref _upstreamHealthTransitions);
    public void RequestReceived()
    {
        Interlocked.Increment(ref _totalRequests);
    }

    public void RequestCompleted(string? site, string? route, string? action, int? statusCode)
    {
        var key = new RequestSeriesKey(ProxyMetricLabelPolicy.NormalizeValue(site), ProxyMetricLabelPolicy.NormalizeValue(route), ProxyMetricLabelPolicy.NormalizeValue(action), ProxyMetricLabelPolicy.StatusClass(statusCode));
        var counter = _requestsByRoute.GetOrAdd(key, static _ => new RequestSeriesCounter());
        Interlocked.Increment(ref counter.Count);
    }

    public void ConnectionAccepted()
    {
        Interlocked.Increment(ref _acceptedConnections);
        Interlocked.Increment(ref _activeConnections);
    }

    public void ConnectionClosed()
    {
        Interlocked.Decrement(ref _activeConnections);
    }

    public void ClientPrematureDisconnect() => Interlocked.Increment(ref _clientPrematureDisconnects);
    public void ClientConnectionClosedByIdleTimeout() => Interlocked.Increment(ref _clientConnectionsClosedByIdleTimeout);
    public void ClientConnectionClosedByMaxRequests() => Interlocked.Increment(ref _clientConnectionsClosedByMaxRequests);
    public void TlsHandshakeAttempted() => Interlocked.Increment(ref _tlsHandshakeAttempts);
    public void TlsHandshakeSucceeded() => Interlocked.Increment(ref _tlsHandshakeSuccesses);
    public void TlsHandshakeFailed() => Interlocked.Increment(ref _tlsHandshakeFailures);
    public void TlsHandshakeTimedOut() => Interlocked.Increment(ref _tlsHandshakeTimeouts);
    public void TlsNoCertificateForSni() => Interlocked.Increment(ref _tlsNoCertificateForSniFailures);
}
