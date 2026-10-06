using System.Collections.ObjectModel;
using BusinessProxyMetricsSnapshot = Mk8.Drava.Application.BLL.ControlPlane.Metrics.ProxyMetricsSnapshot;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyMetricsSnapshotResponseMapper
{
    public static ProxyMetricsSnapshotResponse FromSnapshot(BusinessProxyMetricsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var adminAuth = snapshot.AdminAuth;
        var acmeRenewals = snapshot.AcmeRenewals;
        var clientConnections = snapshot.ClientConnections;
        var clientFailures = snapshot.ClientFailures;
        var configLint = snapshot.ConfigLint;
        var configReloads = snapshot.ConfigReloads;
        var diagnostics = snapshot.Diagnostics;
        var generatedResponses = snapshot.GeneratedResponses;
        var health = snapshot.Health;
        var http2 = snapshot.Http2;
        var http3 = snapshot.Http3;
        var listeners = snapshot.Listeners;
        var requestClassifications = snapshot.RequestClassifications;
        var rejections = snapshot.Rejections;
        var resilience = snapshot.Resilience;
        var routeDiagnostics = snapshot.RouteDiagnostics;
        var tls = snapshot.Tls;
        var traffic = snapshot.Traffic;
        var tunnels = snapshot.Tunnels;
        var upstreamFailureReasons = snapshot.UpstreamFailureReasons;
        var upstreamForwarding = snapshot.UpstreamForwarding;
        var upstreamHttp2 = snapshot.UpstreamHttp2;
        var upstreamHttp3 = snapshot.UpstreamHttp3;
        var upstreamPool = snapshot.UpstreamPool;
        var upstreamSelections = snapshot.UpstreamSelections;
        var upgrades = snapshot.Upgrades;
        return new ProxyMetricsSnapshotResponse(clientConnections.Accepted, clientConnections.Active, traffic.Requests, upstreamForwarding.Successes, upstreamForwarding.Failures, traffic.BytesRead, traffic.BytesWritten, clientFailures.ParseErrors, rejections.MalformedRequests, rejections.UnsupportedRequestFraming, upstreamFailureReasons.MalformedResponses, clientFailures.BodyRelayFailures, upstreamForwarding.BodyRelayFailures, clientFailures.RequestHeadTimeouts, clientFailures.RequestBodyTimeouts, upstreamFailureReasons.ConnectFailures, upstreamFailureReasons.ConnectTimeouts, upstreamFailureReasons.ResponseHeadTimeouts, upstreamFailureReasons.ResponseBodyTimeouts, upstreamFailureReasons.PrematureDisconnects, clientFailures.PrematureDisconnects, generatedResponses.BadGatewayResponses, generatedResponses.GatewayTimeoutResponses, clientFailures.DownstreamWriteTimeouts, tls.HandshakeAttempts, tls.HandshakeSuccesses, tls.HandshakeFailures, tls.HandshakeTimeouts, tls.NoCertificateForSniFailures, clientConnections.ClosedByIdleTimeout, clientConnections.ClosedByMaxRequests, upstreamPool.ConnectionsOpened, upstreamPool.ConnectionsReused, upstreamPool.ConnectionsDiscarded, upstreamPool.IdleConnections, upstreamPool.ActiveConnections, upgrades.RequestsReceived, upgrades.RequestsSucceeded, upgrades.RequestsRejected, upgrades.UpstreamFailures, tunnels.Active, tunnels.Total, tunnels.IdleTimeouts, tunnels.BytesClientToUpstream, tunnels.BytesUpstreamToClient, tunnels.RelayFailures, upstreamSelections.Total, health.NoHealthyUpstreamFailures, health.ChecksAttempted, health.ChecksSucceeded, health.ChecksFailed, health.UpstreamTransitions, upstreamFailureReasons.RequestFailures, diagnostics.RequestIdsGenerated, diagnostics.AccessLogsEmitted, diagnostics.RecentDiagnosticsOverwritten, rejections.ClientConnectionAdmissionRejections, tls.ActiveHandshakes, tls.HandshakeAdmissionRejections, rejections.RateLimitedRequests, rejections.RateLimitedUpgrades, rejections.RequestBodySizeRejections, rejections.ParserLimitRejections, new Dictionary<string, long>(requestClassifications.FailuresByKind), ProxyRequestSeriesSnapshotResponseMapper.FromSnapshots(requestClassifications.ByRoute), configReloads.Successes, configReloads.Failures, adminAuth.Successes, adminAuth.Failures, acmeRenewals.Attempts, acmeRenewals.Successes, acmeRenewals.Failures, resilience.RetryAttempts, resilience.RetryExhausted, ProxyRetrySkippedSnapshotResponseMapper.FromSnapshots(resilience.RetrySkipped), resilience.CircuitOpened, resilience.CircuitHalfOpened, resilience.CircuitClosed, resilience.CircuitRejections, resilience.NoAvailableUpstreamFailures, ProxyUpstreamSelectionSnapshotResponseMapper.FromSnapshots(upstreamSelections.ByUpstream), listeners.ReloadAttempts, listeners.ReloadSuccesses, listeners.ReloadFailures, listeners.ReloadAdded, listeners.ReloadRemoved, listeners.ReloadChanged, listeners.ReloadUnchanged, listeners.StartFailures, listeners.Drains, listeners.ActiveListeners, http2.AcceptedConnections, http2.Requests, http2.ActiveStreams, new Dictionary<string, long>(http2.ProtocolErrors), upstreamHttp2.Requests, upstreamHttp2.AlpnFailures, upstreamHttp2.ProtocolErrors, upstreamHttp3.Requests, upstreamHttp3.ConnectionAttempts, upstreamHttp3.ConnectionSuccesses, upstreamHttp3.ConnectionFailures, upstreamHttp3.PoolConnectionsOpened, upstreamHttp3.PoolConnectionsReused, upstreamHttp3.PoolConnectionsClosed, upstreamHttp3.StreamLimitRejections, upstreamHttp3.ActiveConnections, upstreamHttp3.ActiveStreams, new Dictionary<string, long>(upstreamHttp3.ProtocolErrors), http3.AcceptedConnections, http3.ActiveConnections, http3.Requests, http3.ProxiedRequests, http3.GeneratedResponses, http3.ActiveStreams, http3.StreamResets, http3.StreamedResponses, http3.ActiveResponseStreams, http3.ResponseBytesSent, http3.RequestBodyBytesReceived, http3.ResponseStreamResets, http3.AltSvcEmitted, http3.AltSvcSuppressed, ProxyHttp3RequestOutcomeSnapshotResponseMapper.FromSnapshots(http3.RequestsByOutcome), new Dictionary<string, long>(http3.RejectedRequests), new Dictionary<string, long>(http3.ProtocolErrors), http3.QuicListenerStartSuccesses, http3.QuicListenerStartFailures, http3.ActiveQuicListeners, configLint.Runs, ProxyConfigLintFindingMetricSnapshotResponseMapper.FromSnapshots(configLint.Findings), routeDiagnostics.DryRuns, ProxyRouteDryRunFailureSnapshotResponseMapper.FromSnapshots(routeDiagnostics.DryRunFailures));
    }
}
