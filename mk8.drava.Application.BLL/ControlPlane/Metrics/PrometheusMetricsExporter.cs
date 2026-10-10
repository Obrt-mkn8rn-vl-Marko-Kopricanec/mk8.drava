using System.Text;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Status;
using System.Globalization;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed partial class PrometheusMetricsExporter
{
    public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";
    public string Export(ProxyMetricsExportInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var proxy = input.Metrics;
        var cache = input.CacheStatus;
        var health = input.UpstreamHealth;
        var acme = input.AcmeCertificates;
        var configReloads = proxy.ConfigReloads;
        var configLint = proxy.ConfigLint;
        var adminAuth = proxy.AdminAuth;
        var acmeRenewals = proxy.AcmeRenewals;
        var clientConnections = proxy.ClientConnections;
        var requestClassifications = proxy.RequestClassifications;
        var rejections = proxy.Rejections;
        var routeDiagnostics = proxy.RouteDiagnostics;
        var traffic = proxy.Traffic;
        var builder = new StringBuilder();
        AppendCounter(builder, "mdrava_client_connections_accepted_total", "Accepted downstream client connections.", clientConnections.Accepted);
        AppendGauge(builder, "mdrava_client_connections_active", "Currently active downstream client connections.", clientConnections.Active);
        AppendLabeledCounter(builder, "mdrava_client_connections_rejected_total", "Rejected downstream client connections by bounded reason.", rejections.ClientConnectionAdmissionRejections, new Label("reason", "admission_limit"));
        AppendCounter(builder, "mdrava_requests_total", "HTTP requests received by the dataplane.", traffic.Requests);
        AppendClientProtocolMetrics(builder, input, proxy);
        AppendRouteRequestCounters(builder, input.IncludePerRouteLabels, requestClassifications.ByRoute);
        AppendRequestRejectionCounters(builder, proxy);
        AppendUpstreamAndResilienceMetrics(builder, input, proxy, health);
        AppendCacheMetrics(builder, cache);

        AppendLabeledCounter(builder, "mdrava_config_reloads_total", "Configuration reloads by result.", configReloads.Successes, new Label("result", "success"));
        AppendLabeledCounter(builder, "mdrava_config_reloads_total", null, configReloads.Failures, new Label("result", "failure"));
        AppendCounter(builder, "mdrava_config_lint_runs_total", "Configuration lint runs.", configLint.Runs);
        if (configLint.Findings.Count > 0)
        {
            AppendHelpAndType(builder, "mdrava_config_lint_findings_total", "Configuration lint findings by bounded severity and code.", "counter");
            foreach (var finding in configLint.Findings)
            {
                AppendSample(builder, "mdrava_config_lint_findings_total", finding.Count, new Label("severity", finding.Severity), new Label("code", finding.Code));
            }
        }

        AppendCounter(builder, "mdrava_route_match_dry_runs_total", "Route match dry-run requests.", routeDiagnostics.DryRuns);
        if (routeDiagnostics.DryRunFailures.Count > 0)
        {
            AppendHelpAndType(builder, "mdrava_route_match_dry_run_failures_total", "Route match dry-run non-match or rejection results by bounded reason.", "counter");
            foreach (var failure in routeDiagnostics.DryRunFailures)
            {
                AppendSample(builder, "mdrava_route_match_dry_run_failures_total", failure.Count, new Label("reason", failure.Reason));
            }
        }

        var listeners = proxy.Listeners;
        AppendListenerMetrics(builder, listeners);
        AppendAuthenticationAndAcmeMetrics(builder, adminAuth, acmeRenewals, acme);
        return builder.ToString();
    }

    private static void AppendAuthenticationAndAcmeMetrics(StringBuilder builder, ProxyAdminAuthMetricsSnapshot adminAuth, ProxyAcmeRenewalMetricsSnapshot acmeRenewals, IReadOnlyList<AcmeCertificateLifecycleStatus> acme)
    {
        AppendLabeledCounter(builder, "mdrava_admin_auth_total", "Admin authentication attempts by result.", adminAuth.Successes, new Label("result", "success"));
        AppendLabeledCounter(builder, "mdrava_admin_auth_total", null, adminAuth.Failures, new Label("result", "failure"));
        AppendLabeledCounter(builder, "mdrava_acme_renewals_total", "ACME renewal attempts by result.", acmeRenewals.Attempts, new Label("result", "attempt"));
        AppendLabeledCounter(builder, "mdrava_acme_renewals_total", null, acmeRenewals.Successes, new Label("result", "success"));
        AppendLabeledCounter(builder, "mdrava_acme_renewals_total", null, acmeRenewals.Failures, new Label("result", "failure"));
        AppendAcmeCertificateStatus(builder, acme);
    }

    private static void AppendCacheMetrics(StringBuilder builder, Mk8.Drava.Application.BLL.ControlPlane.Caching.ProxyCacheStatus cache)
    {
        AppendGauge(builder, "mdrava_cache_entries", "Current in-memory response cache entries.", cache.EntryCount);
        AppendGauge(builder, "mdrava_cache_bytes", "Approximate in-memory response cache bytes.", cache.ApproximateBytes);
        AppendCounter(builder, "mdrava_cache_hits_total", "Response cache hits.", cache.HitCount);
        AppendCounter(builder, "mdrava_cache_misses_total", "Response cache misses.", cache.MissCount);
        AppendCounter(builder, "mdrava_cache_stores_total", "Stored response cache entries.", cache.StoreCount);
        AppendCounter(builder, "mdrava_cache_evictions_total", "Evicted response cache entries.", cache.EvictionCount);
        if (cache.Rejections.Count > 0)
        {
            AppendHelpAndType(builder, "mdrava_cache_store_rejections_total", "Response cache store rejections by bounded reason.", "counter");
        }

        foreach (var rejection in cache.Rejections)
        {
            AppendSample(builder, "mdrava_cache_store_rejections_total", rejection.Count, new Label("reason", rejection.Reason));
        }
    }

    private static void AppendListenerMetrics(StringBuilder builder, ProxyListenerMetricsSnapshot listeners)
    {
        AppendLabeledCounter(builder, "mdrava_listener_reloads_total", "Proxy listener reload attempts by result.", listeners.ReloadSuccesses, new Label("result", "success"));
        AppendLabeledCounter(builder, "mdrava_listener_reloads_total", null, listeners.ReloadFailures, new Label("result", "failure"));
        AppendCounter(builder, "mdrava_listener_reload_attempts_total", "Proxy listener reload attempts.", listeners.ReloadAttempts);
        AppendLabeledCounter(builder, "mdrava_listener_reload_changes_total", "Proxy listener reload changes by bounded action.", listeners.ReloadAdded, new Label("action", "added"));
        AppendLabeledCounter(builder, "mdrava_listener_reload_changes_total", null, listeners.ReloadRemoved, new Label("action", "removed"));
        AppendLabeledCounter(builder, "mdrava_listener_reload_changes_total", null, listeners.ReloadChanged, new Label("action", "changed"));
        AppendLabeledCounter(builder, "mdrava_listener_reload_changes_total", null, listeners.ReloadUnchanged, new Label("action", "unchanged"));
        AppendCounter(builder, "mdrava_listener_start_failures_total", "Proxy listener start failures.", listeners.StartFailures);
        AppendCounter(builder, "mdrava_listener_drains_total", "Proxy listener drains after reload removal or replacement.", listeners.Drains);
        AppendGauge(builder, "mdrava_listeners_active", "Currently active proxy listeners.", listeners.ActiveListeners);
    }

    private static void AppendHttp2ClientMetrics(StringBuilder builder, ProxyHttp2MetricsSnapshot http2)
    {
        AppendCounter(builder, "mdrava_http2_connections_accepted_total", "Accepted HTTP/2 downstream client connections.", http2.AcceptedConnections);
        AppendCounter(builder, "mdrava_http2_requests_total", "HTTP/2 requests received by the dataplane.", http2.Requests);
        AppendGauge(builder, "mdrava_http2_streams_active", "Currently active HTTP/2 streams.", http2.ActiveStreams);
        if (http2.ProtocolErrors.Count > 0)
        {
            AppendHelpAndType(builder, "mdrava_http2_protocol_errors_total", "HTTP/2 protocol errors by bounded reason.", "counter");
            foreach (var error in http2.ProtocolErrors.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                AppendSample(builder, "mdrava_http2_protocol_errors_total", error.Value, new Label("reason", error.Key));
            }
        }
    }

    private static void AppendResilienceAndPoolMetrics(StringBuilder builder, ProxyResilienceMetricsSnapshot resilience, ProxyUpstreamPoolMetricsSnapshot upstreamPool, ProxyHealthMetricsSnapshot healthMetrics, bool includePerUpstreamLabels, IReadOnlyList<ProxyUpstreamStatus> health)
    {
        AppendCounter(builder, "mdrava_retry_attempts_total", "Retry attempts after an initial failed upstream attempt.", resilience.RetryAttempts);
        AppendCounter(builder, "mdrava_retry_exhausted_total", "Requests that exhausted their configured retry attempts.", resilience.RetryExhausted);
        if (resilience.RetrySkipped.Count > 0)
        {
            AppendHelpAndType(builder, "mdrava_retry_skipped_total", "Retries skipped by bounded reason.", "counter");
        }

        foreach (var skipped in resilience.RetrySkipped)
        {
            AppendSample(builder, "mdrava_retry_skipped_total", skipped.Count, new Label("reason", skipped.Reason));
        }

        AppendLabeledCounter(builder, "mdrava_circuit_transitions_total", "Circuit breaker transitions by state.", resilience.CircuitOpened, new Label("state", "open"));
        AppendLabeledCounter(builder, "mdrava_circuit_transitions_total", null, resilience.CircuitHalfOpened, new Label("state", "half_open"));
        AppendLabeledCounter(builder, "mdrava_circuit_transitions_total", null, resilience.CircuitClosed, new Label("state", "closed"));
        AppendCounter(builder, "mdrava_circuit_rejections_total", "Requests rejected by open or saturated half-open circuits.", resilience.CircuitRejections);
        AppendGauge(builder, "mdrava_upstream_connections_active", "Active borrowed upstream connections.", upstreamPool.ActiveConnections);
        AppendGauge(builder, "mdrava_upstream_connections_idle", "Idle reusable upstream connections.", upstreamPool.IdleConnections);
        AppendCounter(builder, "mdrava_upstream_connections_opened_total", "Opened upstream connections.", upstreamPool.ConnectionsOpened);
        AppendCounter(builder, "mdrava_upstream_connections_reused_total", "Reused upstream connections.", upstreamPool.ConnectionsReused);
        AppendCounter(builder, "mdrava_upstream_connections_discarded_total", "Discarded upstream connections.", upstreamPool.ConnectionsDiscarded);
        AppendLabeledCounter(builder, "mdrava_health_checks_total", "Health checks by result.", healthMetrics.ChecksAttempted, new Label("result", "attempted"));
        AppendLabeledCounter(builder, "mdrava_health_checks_total", null, healthMetrics.ChecksSucceeded, new Label("result", "success"));
        AppendLabeledCounter(builder, "mdrava_health_checks_total", null, healthMetrics.ChecksFailed, new Label("result", "failure"));
        AppendCounter(builder, "mdrava_upstream_health_transitions_total", "Upstream health state transitions.", healthMetrics.UpstreamTransitions);
        AppendUpstreamHealth(builder, includePerUpstreamLabels, health);
    }

    private static void AppendUpstreamSelectionCounters(StringBuilder builder, bool includePerUpstreamLabels, IReadOnlyList<ProxyUpstreamSelectionSnapshot> selections)
    {
        if (!includePerUpstreamLabels || selections.Count == 0)
        {
            return;
        }

        AppendHelpAndType(builder, "mdrava_upstream_selections_total", "Selected upstream count by bounded upstream labels.", "counter");
        foreach (var selection in selections)
        {
            AppendSample(builder, "mdrava_upstream_selections_total", selection.Count, new Label("route", selection.Route), new Label("upstream", selection.Upstream), new Label("scheme", selection.Scheme), new Label("protocol", selection.Protocol));
        }
    }

    private static void AppendUpstreamHealth(StringBuilder builder, bool includePerUpstreamLabels, IReadOnlyList<ProxyUpstreamStatus> health)
    {
        if (!includePerUpstreamLabels)
        {
            return;
        }

        AppendHelpAndType(builder, "mdrava_upstream_health_up", "Current upstream health status, 1 for healthy and 0 otherwise.", "gauge");
        foreach (var upstream in health)
        {
            var value = upstream.HealthState == UpstreamHealthState.Healthy ? 1 : 0;
            AppendSample(builder, "mdrava_upstream_health_up", value, new Label("route", upstream.RouteName), new Label("upstream", upstream.UpstreamName), new Label("scheme", upstream.Scheme), new Label("protocol", upstream.Protocol), new Label("state", UpstreamHealthStateText.FromState(upstream.HealthState)));
        }
    }

    private static void AppendAcmeCertificateStatus(StringBuilder builder, IReadOnlyList<AcmeCertificateLifecycleStatus> statuses)
    {
        AppendHelpAndType(builder, "mdrava_acme_certificates", "Configured ACME certificate lifecycle statuses by bounded result.", "gauge");
        foreach (var group in statuses.GroupBy(static status => new { status.LastResult, status.Active }))
        {
            AppendSample(builder, "mdrava_acme_certificates", group.Count(), new Label("result", group.Key.LastResult), new Label("active", group.Key.Active ? "true" : "false"));
        }
    }

    private static void AppendCounter(StringBuilder builder, string name, string help, long value)
    {
        AppendHelpAndType(builder, name, help, "counter");
        AppendSample(builder, name, value);
    }

    private static void AppendGauge(StringBuilder builder, string name, string help, long value)
    {
        AppendHelpAndType(builder, name, help, "gauge");
        AppendSample(builder, name, value);
    }

    private static void AppendLabeledCounter(StringBuilder builder, string name, string? help, long value, params Label[] labels)
    {
        if (help is not null)
        {
            AppendHelpAndType(builder, name, help, "counter");
        }

        AppendSample(builder, name, value, labels);
    }

    private static void AppendHelpAndType(StringBuilder builder, string name, string help, string type)
    {
        builder.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
        builder.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
    }

    private static void AppendSample(StringBuilder builder, string name, long value, params Label[] labels)
    {
        builder.Append(name);
        if (labels.Length > 0)
        {
            builder.Append('{');
            for (var index = 0; index < labels.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append(labels[index].Name).Append("=\"").Append(EscapeLabelValue(ProxyMetricLabelPolicy.NormalizeValue(labels[index].Value))).Append('"');
            }

            builder.Append('}');
        }

        builder.Append(' ').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    private static string EscapeLabelValue(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private readonly record struct Label(string Name, string Value);
    private static void AppendClientProtocolMetrics(StringBuilder builder, ProxyMetricsExportInput input, ProxyMetricsSnapshot proxy)
    {
        var http2 = proxy.Http2;
        AppendHttp2ClientMetrics(builder, http2);

        var http3 = proxy.Http3;
        AppendCounter(builder, "mdrava_http3_connections_accepted_total", "Accepted HTTP/3 downstream client connections.", http3.AcceptedConnections);
        AppendGauge(builder, "mdrava_http3_connections_active", "Currently active HTTP/3 client connections.", http3.ActiveConnections);
        AppendCounter(builder, "mdrava_http3_requests_total", "HTTP/3 requests received by the dataplane.", http3.Requests);
        if (http3.RequestsByOutcome.Count > 0)
        {
            AppendHelpAndType(builder, "mdrava_http3_requests_by_outcome_total", "HTTP/3 requests by bounded method, outcome, and status class.", "counter");
            foreach (var request in http3.RequestsByOutcome)
            {
                AppendSample(builder, "mdrava_http3_requests_by_outcome_total", request.Count, new Label("method", request.Method), new Label("outcome", request.Outcome), new Label("status_class", request.StatusClass));
            }
        }

        AppendCounter(builder, "mdrava_http3_proxied_requests_total", "HTTP/3 requests sent through proxy routes.", http3.ProxiedRequests);
        AppendCounter(builder, "mdrava_http3_generated_responses_total", "HTTP/3 generated route or control responses.", http3.GeneratedResponses);
        AppendGauge(builder, "mdrava_http3_streams_active", "Currently active HTTP/3 request streams.", http3.ActiveStreams);
        AppendCounter(builder, "mdrava_http3_stream_resets_total", "HTTP/3 request stream resets or cancellations.", http3.StreamResets);
        AppendCounter(builder, "mdrava_http3_streamed_responses_total", "HTTP/3 proxied responses streamed over DATA frames.", http3.StreamedResponses);
        AppendGauge(builder, "mdrava_http3_response_streams_active", "Currently active HTTP/3 response body streams.", http3.ActiveResponseStreams);
        AppendCounter(builder, "mdrava_http3_response_bytes_sent_total", "HTTP/3 response body bytes sent in DATA frames.", http3.ResponseBytesSent);
        AppendCounter(builder, "mdrava_http3_request_body_bytes_received_total", "HTTP/3 request body bytes accepted from DATA frames.", http3.RequestBodyBytesReceived);
        AppendCounter(builder, "mdrava_http3_response_stream_resets_total", "HTTP/3 response stream cancellations or write failures.", http3.ResponseStreamResets);
        AppendCounter(builder, "mdrava_http3_alt_svc_emitted_total", "HTTP/3 Alt-Svc headers emitted on proxy responses.", http3.AltSvcEmitted);
        AppendCounter(builder, "mdrava_http3_alt_svc_suppressed_total", "HTTP/3 Alt-Svc opportunities suppressed because HTTP/3 was disabled or not ready.", http3.AltSvcSuppressed);
        AppendGauge(builder, "mdrava_http3_default_enabled_listeners", "Configured default-enabled HTTP/3 proxy listeners.", input.DefaultEnabledHttp3ListenerCount);
        AppendGauge(builder, "mdrava_http3_qpack_dynamic_table_capacity", "Configured HTTP/3 QPACK dynamic table capacity. MDRAVA advertises zero for bounded static-table operation.", 0);
        AppendGauge(builder, "mdrava_http3_qpack_blocked_streams", "Configured HTTP/3 QPACK blocked streams. MDRAVA advertises zero to avoid blocked-stream accumulation.", 0);
        AppendGauge(builder, "mdrava_http3_request_body_streaming_enabled", "Whether HTTP/3 request body streaming is enabled for eligible proxy listeners.", input.Http3RequestBodyStreamingEnabled ? 1 : 0);
        AppendGauge(builder, "mdrava_quic_listeners_active", "Currently active HTTP/3 QUIC listeners.", http3.ActiveQuicListeners);
        AppendLabeledCounter(builder, "mdrava_quic_listener_starts_total", "HTTP/3 QUIC listener starts by result.", http3.QuicListenerStartSuccesses, new Label("result", "success"));
        AppendLabeledCounter(builder, "mdrava_quic_listener_starts_total", null, http3.QuicListenerStartFailures, new Label("result", "failure"));
        if (http3.RejectedRequests.Count > 0)
        {
            AppendHelpAndType(builder, "mdrava_http3_rejected_requests_total", "HTTP/3 request rejections by bounded reason.", "counter");
            foreach (var rejection in http3.RejectedRequests.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                AppendSample(builder, "mdrava_http3_rejected_requests_total", rejection.Value, new Label("reason", rejection.Key));
            }
        }

        if (http3.ProtocolErrors.Count > 0)
        {
            AppendHelpAndType(builder, "mdrava_http3_protocol_errors_total", "HTTP/3 protocol errors by bounded reason.", "counter");
            foreach (var error in http3.ProtocolErrors.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                AppendSample(builder, "mdrava_http3_protocol_errors_total", error.Value, new Label("reason", error.Key));
            }
        }
    }

    private static void AppendUpstreamAndResilienceMetrics(StringBuilder builder, ProxyMetricsExportInput input, ProxyMetricsSnapshot proxy, IReadOnlyList<ProxyUpstreamStatus> health)
    {
        var upstreamHttp3 = proxy.UpstreamHttp3;
        var upstreamHttp2 = proxy.UpstreamHttp2;
        var upstreamPool = proxy.UpstreamPool;
        var healthMetrics = proxy.Health;
        var resilience = proxy.Resilience;
        var upstreamSelections = proxy.UpstreamSelections;
        var upstreamFailureReasons = proxy.UpstreamFailureReasons;
        AppendCounter(builder, "mdrava_upstream_request_attempts_total", "Selected upstream request attempts.", upstreamSelections.Total);
        AppendCounter(builder, "mdrava_upstream_http2_requests_total", "Upstream HTTP/2 request attempts.", upstreamHttp2.Requests);
        AppendCounter(builder, "mdrava_upstream_http3_requests_total", "Upstream HTTP/3 request attempts.", upstreamHttp3.Requests);
        AppendCounter(builder, "mdrava_upstream_http3_connection_attempts_total", "Upstream HTTP/3 QUIC connection attempts.", upstreamHttp3.ConnectionAttempts);
        AppendCounter(builder, "mdrava_upstream_http3_connection_successes_total", "Successful upstream HTTP/3 QUIC connections.", upstreamHttp3.ConnectionSuccesses);
        AppendCounter(builder, "mdrava_upstream_http3_connection_failures_total", "Failed upstream HTTP/3 QUIC connections.", upstreamHttp3.ConnectionFailures);
        AppendCounter(builder, "mdrava_upstream_http3_pool_connections_opened_total", "Upstream HTTP/3 pool connections opened.", upstreamHttp3.PoolConnectionsOpened);
        AppendCounter(builder, "mdrava_upstream_http3_pool_connections_reused_total", "Upstream HTTP/3 pool connection reuses.", upstreamHttp3.PoolConnectionsReused);
        AppendCounter(builder, "mdrava_upstream_http3_pool_connections_closed_total", "Upstream HTTP/3 pool connections closed.", upstreamHttp3.PoolConnectionsClosed);
        AppendCounter(builder, "mdrava_upstream_http3_stream_limit_rejections_total", "Upstream HTTP/3 stream limit rejections.", upstreamHttp3.StreamLimitRejections);
        AppendGauge(builder, "mdrava_upstream_http3_multiplexing_enabled", "Whether upstream HTTP/3 multiplexing is enabled.", upstreamHttp3.Requests > 0 || input.UpstreamHttp3MultiplexingConfigured ? 1 : 0);
        AppendGauge(builder, "mdrava_upstream_http3_connections_active", "Active upstream HTTP/3 QUIC connections.", upstreamHttp3.ActiveConnections);
        AppendGauge(builder, "mdrava_upstream_http3_streams_active", "Active upstream HTTP/3 streams.", upstreamHttp3.ActiveStreams);
        AppendUpstreamSelectionCounters(builder, input.IncludePerUpstreamLabels, upstreamSelections.ByUpstream);
        AppendLabeledCounter(builder, "mdrava_upstream_failures_total", "Upstream failures by bounded reason.", upstreamFailureReasons.ConnectFailures, new Label("reason", "connect_failure"));
        AppendLabeledCounter(builder, "mdrava_upstream_failures_total", null, upstreamFailureReasons.ConnectTimeouts, new Label("reason", "connect_timeout"));
        AppendLabeledCounter(builder, "mdrava_upstream_failures_total", null, upstreamFailureReasons.ResponseHeadTimeouts, new Label("reason", "response_head_timeout"));
        AppendLabeledCounter(builder, "mdrava_upstream_failures_total", null, upstreamFailureReasons.ResponseBodyTimeouts, new Label("reason", "response_body_timeout"));
        AppendLabeledCounter(builder, "mdrava_upstream_failures_total", null, upstreamFailureReasons.MalformedResponses, new Label("reason", "malformed_response"));
        AppendLabeledCounter(builder, "mdrava_upstream_failures_total", null, upstreamFailureReasons.PrematureDisconnects, new Label("reason", "premature_disconnect"));
        AppendLabeledCounter(builder, "mdrava_upstream_failures_total", null, healthMetrics.NoHealthyUpstreamFailures, new Label("reason", "no_healthy_upstream"));
        AppendLabeledCounter(builder, "mdrava_upstream_failures_total", null, resilience.NoAvailableUpstreamFailures, new Label("reason", "no_available_upstream"));
        AppendLabeledCounter(builder, "mdrava_upstream_failures_total", null, upstreamFailureReasons.RequestFailures, new Label("reason", "request_failure"));
        AppendLabeledCounter(builder, "mdrava_upstream_http2_failures_total", "Upstream HTTP/2 failures by bounded reason.", upstreamHttp2.AlpnFailures, new Label("reason", "alpn_failure"));
        AppendLabeledCounter(builder, "mdrava_upstream_http2_failures_total", null, upstreamHttp2.ProtocolErrors, new Label("reason", "protocol_error"));
        if (upstreamHttp3.ProtocolErrors.Count > 0)
        {
            AppendHelpAndType(builder, "mdrava_upstream_http3_protocol_errors_total", "Upstream HTTP/3 protocol errors by bounded reason.", "counter");
            foreach (var error in upstreamHttp3.ProtocolErrors.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                AppendSample(builder, "mdrava_upstream_http3_protocol_errors_total", error.Value, new Label("reason", error.Key));
            }
        }

        AppendResilienceAndPoolMetrics(builder, resilience, upstreamPool, healthMetrics, includePerUpstreamLabels: input.IncludePerUpstreamLabels, health);
    }

    private static void AppendRouteRequestCounters(StringBuilder builder, bool includePerRouteLabels, IReadOnlyList<ProxyRequestSeriesSnapshot> requests)
    {
        AppendHelpAndType(builder, "mdrava_route_requests_total", "Completed requests by bounded route/action/status labels.", "counter");
        IEnumerable<ProxyRequestSeriesSnapshot> series = includePerRouteLabels ? requests : requests.GroupBy(static request => new { request.Action, request.StatusClass }).Select(static group => new ProxyRequestSeriesSnapshot("all", "all", group.Key.Action, group.Key.StatusClass, group.Sum(static item => item.Count)));
        foreach (var request in series)
        {
            var labels = includePerRouteLabels ? new[]
            {
                new Label("site", request.Site),
                new Label("route", request.Route),
                new Label("action", request.Action),
                new Label("status_class", request.StatusClass)
            }

            : [new Label("action", request.Action), new Label("status_class", request.StatusClass)];
            AppendSample(builder, "mdrava_route_requests_total", request.Count, labels);
        }
    }

    private static void AppendRequestRejectionCounters(StringBuilder builder, ProxyMetricsSnapshot proxy)
    {
        var rejections = proxy.Rejections;
        AppendLabeledCounter(builder, "mdrava_request_rejections_total", "Request rejections by bounded reason.", rejections.RateLimitedRequests, new Label("reason", "rate_limited"));
        AppendLabeledCounter(builder, "mdrava_request_rejections_total", null, rejections.RateLimitedUpgrades, new Label("reason", "upgrade_rate_limited"));
        AppendLabeledCounter(builder, "mdrava_request_rejections_total", null, rejections.RequestBodySizeRejections, new Label("reason", "body_too_large"));
        AppendLabeledCounter(builder, "mdrava_request_rejections_total", null, rejections.ParserLimitRejections, new Label("reason", "parser_limit"));
        AppendLabeledCounter(builder, "mdrava_request_rejections_total", null, rejections.MalformedRequests, new Label("reason", "malformed"));
        AppendLabeledCounter(builder, "mdrava_request_rejections_total", null, rejections.UnsupportedRequestFraming, new Label("reason", "unsupported_framing"));
    }
}
