using System.Net;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;
using Mk8.Drava.CompatibilityTests.LegacyApi.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class RouteDiagnosticsTests
{
    private const string AdminToken = "phase-27-admin-token";
    public static void DryRunMatchesSameRouteAsDataplaneMatcher()
    {
        var options = BaseOptions([ProxyRoute("api", "diag.test", "/api"), ProxyRoute("default", "diag.test", "/")]);
        var service = CreateRouteService(options, out _, out _);
        var matcher = new SingleUpstreamRouteMatcher();
        var requestHead = Request("GET", "/api/users?id=1", "/api/users", "diag.test");
        var routes = Snapshot(options).Routes;
        var direct = matcher.Match(routes.Select(static route => new RouteMatchCandidate(route.Host, route.PathPrefix)).ToArray(), new RouteMatchRequest(requestHead.Host, requestHead.Path));
        var dryRun = service.Explain(new RouteMatchDryRunRequest("http", "diag.test", 8080, "GET", "/api/users", "?id=1", NoHeaders(), null, null));
        var matched = Matched(dryRun);
        AssertEx.NotNull(direct);
        AssertEx.Equal(routes[direct!.RouteIndex].Name, matched.Route.Name);
        AssertEx.Equal("proxy", matched.EffectiveAction);
    }

    public static void DryRunDoesNotPerformUpstreamIoOrMutateRetryCircuitOrCacheState()
    {
        var options = BaseOptions([ProxyRoute("api", "diag.test", "/api", upstreamPort: 65500, cache: CachePolicy(), retry: RetryPolicy(), circuitBreaker: new ProxyCircuitBreakerOptions { Enabled = true })]);
        var service = CreateRouteService(options, out var store, out var metrics);
        var cache = new ResponseCacheStore(TimeProvider.System);
        var beforeMetrics = metrics.Snapshot();
        var beforeCache = CacheStatus(cache, store.Snapshot);
        var result = service.Explain(new RouteMatchDryRunRequest("http", "diag.test", 8080, "GET", "/api/users", "", NoHeaders(), null, null));
        var afterMetrics = metrics.Snapshot();
        var afterCache = CacheStatus(cache, store.Snapshot);
        AssertEx.True(Matched(result).WouldProxy);
        AssertEx.Equal(0L, afterMetrics.Resilience.RetryAttempts - beforeMetrics.Resilience.RetryAttempts);
        AssertEx.Equal(0L, afterMetrics.Resilience.CircuitRejections - beforeMetrics.Resilience.CircuitRejections);
        AssertEx.Equal(beforeCache.HitCount, afterCache.HitCount);
        AssertEx.Equal(beforeCache.MissCount, afterCache.MissCount);
        AssertEx.Equal(1L, afterMetrics.RouteDiagnostics.DryRuns - beforeMetrics.RouteDiagnostics.DryRuns);
    }

    public static void DryRunReportsNoMatchReason()
    {
        var service = CreateRouteService(BaseOptions([ProxyRoute("api", "diag.test", "/api")]), out _, out _);
        var result = service.Explain(new RouteMatchDryRunRequest("http", "other.test", 8080, "GET", "/api", "", NoHeaders(), null, null));
        var noRoute = NoMatchingRoute(result);
        AssertEx.Equal("no_matching_route", noRoute.NoMatchReason);
    }

    public static void DryRunReportsPathRewriteResult()
    {
        var route = ProxyRoute("api", "diag.test", "/public", pathRewrite: new ProxyPathRewriteOptions { StripPrefix = "/public" });
        var service = CreateRouteService(BaseOptions([route]), out _, out _);
        var result = service.Explain(new RouteMatchDryRunRequest("http", "diag.test", 8080, "GET", "/public/api/users", "id=1", NoHeaders(), null, null));
        AssertEx.Equal("/api/users?id=1", Matched(result).RewrittenTarget);
    }

    public static void RoutingRuntimeMappersRejectNullInputs()
    {
        var snapshot = Snapshot(BaseOptions([ProxyRoute("api", "diag.test", "/api")]));
        var route = snapshot.Routes[0];
        var listener = snapshot.Listeners[0];
        var requestHead = Request("GET", "/api/users", "/api/users", "diag.test");
        AssertEx.Throws<ArgumentNullException>(() => ProxyPathRewriteRuntimeMapper.ToPolicyInput(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteActionRuntimeMapper.ToPolicyInput(null!, requestHead, listener, false));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteActionRuntimeMapper.ToPolicyInput(route, null!, listener, false));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteActionRuntimeMapper.ToPolicyInput(route, requestHead, null!, false));
    }

    public static void RequestContextRuntimeMapperRejectsNullInputs()
    {
        var snapshot = Snapshot(BaseOptions([ProxyRoute("api", "diag.test", "/api")]));
        var route = snapshot.Routes[0];
        var upstream = route.Upstreams[0];
        AssertEx.Throws<ArgumentNullException>(() => ProxyRequestContextRuntimeMapper.ToTransport(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRequestContextRuntimeMapper.ToRequestRoute(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRequestContextRuntimeMapper.ToRequestUpstream(null!));
        AssertEx.Equal("api", ProxyRequestContextRuntimeMapper.ToRequestRoute(route).Name);
        AssertEx.Equal("local", ProxyRequestContextRuntimeMapper.ToRequestUpstream(upstream).Name);
    }

    public static void DryRunCanSelectHttp3ProtocolListener()
    {
        var options = new ProxyOptions
        {
            Listeners = [new ListenerOptions
            {
                Name = "web",
                Address = "127.0.0.1",
                Port = 8443,
                Transport = "https",
                Protocols = "http1AndHttp2AndHttp3",
                Http3Enablement = "default",
                DefaultCertificateId = "home-cert"
            }

            ],
            Routes = [ProxyRoute("api", "diag.test", "/api")]
        };
        var service = CreateRouteService(options, out _, out _);
        var result = service.Explain(new RouteMatchDryRunRequest("https", "diag.test", 8443, "GET", "/api", "", NoHeaders(), null, "web", "http3"));
        var matched = Matched(result);
        AssertEx.Equal("api", matched.Route.Name);
        var listener = matched.Listener;
        AssertEx.Equal("https", listener.Transport);
        AssertEx.True(listener.Protocols.Contains("Http3", StringComparison.Ordinal));
    }

    public static void ListenerSelectorReadsListenersWithoutConfigurationSnapshot()
    {
        var listeners = new IProxyRouteDiagnosticsListener[]
        {
            RouteDiagnosticsListener("plain", "http", 8080, RuntimeListenerProtocols.Http1, false),
            RouteDiagnosticsListener("secure", "https", 8443, RuntimeListenerProtocols.Http1 | RuntimeListenerProtocols.Http2 | RuntimeListenerProtocols.Http3, true)
        };
        var selected = ProxyRouteDiagnosticsListenerSelector.Select(listeners, "secure", "https", 8443, "http3");
        AssertEx.NotNull(selected);
        AssertEx.Equal("secure", selected!.Name);
        AssertEx.True(selected.Http3EnabledForTraffic);
    }

    public static void DryRunReportsGeneratedRouteActions()
    {
        var redirect = new ProxyRouteOptions
        {
            Name = "old",
            Host = "diag.test",
            PathPrefix = "/old",
            Action = "redirect",
            Redirect = new ProxyRedirectOptions
            {
                TargetPath = "/new",
                StatusCode = 308
            }
        };
        var disabled = new ProxyRouteOptions
        {
            Name = "disabled",
            Host = "diag.test",
            PathPrefix = "/disabled",
            Action = "staticResponse",
            Maintenance = new ProxyMaintenanceOptions
            {
                Enabled = true
            }
        };
        var staticRoute = new ProxyRouteOptions
        {
            Name = "static",
            Host = "diag.test",
            PathPrefix = "/static",
            Action = "staticResponse",
            StaticResponse = new ProxyStaticResponseOptions
            {
                StatusCode = 410,
                Body = "Gone"
            }
        };
        var service = CreateRouteService(BaseOptions([redirect, disabled, staticRoute]), out _, out _);
        var redirectResult = service.Explain(new RouteMatchDryRunRequest("http", "diag.test", 8080, "GET", "/old", "", NoHeaders(), null, null));
        var maintenanceResult = service.Explain(new RouteMatchDryRunRequest("http", "diag.test", 8080, "GET", "/disabled", "", NoHeaders(), null, null));
        var staticResult = service.Explain(new RouteMatchDryRunRequest("http", "diag.test", 8080, "GET", "/static", "", NoHeaders(), null, null));
        var redirectMatch = Matched(redirectResult);
        var maintenanceMatch = Matched(maintenanceResult);
        var staticMatch = Matched(staticResult);
        AssertEx.Equal("redirect", redirectMatch.EffectiveAction);
        AssertEx.Equal(308, redirectMatch.GeneratedStatusCode!.Value);
        AssertEx.Equal("maintenance", maintenanceMatch.EffectiveAction);
        AssertEx.Equal(503, maintenanceMatch.GeneratedStatusCode!.Value);
        AssertEx.Equal("staticResponse", staticMatch.EffectiveAction);
        AssertEx.Equal(410, staticMatch.GeneratedStatusCode!.Value);
    }

    public static void DryRunPolicyExplainerUsesNamedPolicyOutcomes()
    {
        var route = RouteDiagnosticsRoute("api", "diag.test", "/api", cacheEnabled: true, retryEnabled: true, circuitBreakerEnabled: true);
        var getRequest = new ProxyRouteDiagnosticsRequestHead("GET", "/api", "/api", "HTTP/1.1", "diag.test", ProxyRouteDiagnosticsRequestFraming.None, []);
        var postRequest = new ProxyRouteDiagnosticsRequestHead("POST", "/api", "/api", "HTTP/1.1", "diag.test", ProxyRouteDiagnosticsRequestFraming.None, []);
        var disabled = ProxyRouteDiagnosticsPolicyExplainer.Disabled("no_route");
        var cacheEligible = ProxyRouteDiagnosticsPolicyExplainer.ExplainCache(route, getRequest, wouldProxy: true);
        var retryBlocked = ProxyRouteDiagnosticsPolicyExplainer.ExplainRetry(route, postRequest, wouldProxy: true);
        var circuitBlocked = ProxyRouteDiagnosticsPolicyExplainer.ExplainCircuitBreaker(route, wouldProxy: false);
        AssertEx.Equal(RouteMatchDryRunPolicy.Disabled("no_route"), disabled);
        AssertEx.Equal(RouteMatchDryRunPolicy.EnabledAndApplies("eligible_before_origin_response"), cacheEligible);
        AssertEx.Equal(RouteMatchDryRunPolicy.EnabledButBlocked("method_not_retryable"), retryBlocked);
        AssertEx.Equal(RouteMatchDryRunPolicy.EnabledButBlocked("not_proxy_action"), circuitBlocked);
    }

    public static void RouteDiagnosticsActionDecisionNamesProxyAndGeneratedResponses()
    {
        var proxy = ProxyRouteDiagnosticsActionDecision.Proxy;
        var generated = ProxyRouteDiagnosticsActionDecision.GeneratedResponse(308);
        AssertEx.True(proxy.ShouldProxy);
        AssertEx.Equal<int?>(null, proxy.GeneratedStatusCode);
        AssertEx.False(generated.ShouldProxy);
        AssertEx.Equal(308, generated.GeneratedStatusCode!.Value);
        AssertEx.Throws<ArgumentException>(() => new ProxyRouteDiagnosticsActionDecision(ShouldProxy: true, GeneratedStatusCode: 308));
        AssertEx.Throws<ArgumentException>(() => new ProxyRouteDiagnosticsActionDecision(ShouldProxy: false, GeneratedStatusCode: null));
    }

    public static void RouteDiagnosticsRoutePoliciesRejectNullFacts()
    {
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsMaintenancePolicy(false, null, null!, ""));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsMaintenancePolicy(false, null, "text/plain", null!));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsCanonicalHostPolicy(false, null!, 308));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRedirectPolicy(302, null!, "", false));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRedirectPolicy(302, "", null!, false));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsStaticResponse(200, null!, ""));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsStaticResponse(200, "text/plain", null!));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsPathRewrite(null!, "", ""));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsPathRewrite("", null!, ""));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsPathRewrite("", "", null!));
    }

    public static void RouteDiagnosticsDryRunOutputsRejectNullFacts()
    {
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunRoute(null!, "api", "example.test", "/"));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunRoute("site", null!, "example.test", "/"));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunRoute("site", "api", null!, "/"));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunRoute("site", "api", "example.test", null!));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunListener(null!, "http", "127.0.0.1", 8080, "Http1"));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunListener("plain", null!, "127.0.0.1", 8080, "Http1"));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunListener("plain", "http", null!, 8080, "Http1"));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunListener("plain", "http", "127.0.0.1", 8080, null!));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunUpstream(null!, "http", RuntimeUpstreamProtocol.Http1, "127.0.0.1:5000", 1, "selected"));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunUpstream("local", null!, RuntimeUpstreamProtocol.Http1, "127.0.0.1:5000", 1, "selected"));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunUpstream("local", "http", null!, "127.0.0.1:5000", 1, "selected"));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunUpstream("local", "http", RuntimeUpstreamProtocol.Http1, null!, 1, "selected"));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunUpstream("local", "http", RuntimeUpstreamProtocol.Http1, "127.0.0.1:5000", 1, null!));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunFinding(null!, "code", "message"));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunFinding("warning", null!, "message"));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunFinding("warning", "code", null!));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunPolicy(false, false, null!));
        AssertEx.Throws<ArgumentNullException>(() => RouteMatchDryRunPolicy.Disabled(null!));
    }

    public static void RouteDiagnosticsActionPolicyUsesSharedPolicyRedirects()
    {
        var route = RouteDiagnosticsRoute("api", "old.test", "/")with
        {
            HttpsRedirect = new ProxyRouteDiagnosticsHttpsRedirectPolicy(true, 307, 8443),
            CanonicalHost = new ProxyRouteDiagnosticsCanonicalHostPolicy(true, "canonical.test", 308)
        };
        var requestHead = new ProxyRouteDiagnosticsRequestHead("GET", "/resource?id=1", "/resource", "HTTP/1.1", "old.test", ProxyRouteDiagnosticsRequestFraming.None, []);
        var listener = RouteDiagnosticsListener("plain", "http", 8080, RuntimeListenerProtocols.Http1, false);
        var policy = new ProxyRouteDiagnosticsActionPolicyAdapter();
        var decision = policy.Evaluate(route, requestHead, listener, isUpgradeRequest: false);
        AssertEx.False(decision.ShouldProxy);
        AssertEx.Equal(308, decision.GeneratedStatusCode!.Value);
    }

    public static void GeneratedRouteResponseHeaderPolicyBuildsFramedHeaders()
    {
        var responseHeaders = new List<ProxyHeaderField>
        {
            new("Location", "/next"),
            new("Connection", "close"),
            new("Keep-Alive", "timeout=5")
        };
        var response = new GeneratedRouteResponse(302, "Found", "text/plain", "go", responseHeaders);
        responseHeaders.Clear();
        var headers = GeneratedRouteResponseHeaderPolicy.BuildFramedResponseHeaders(response, "req-456", 2);
        AssertEx.Equal("text/plain", headers.Single(static header => header.Name == "content-type").Value);
        AssertEx.Equal("req-456", headers.Single(static header => header.Name == "x-request-id").Value);
        AssertEx.Equal("2", headers.Single(static header => header.Name == "content-length").Value);
        AssertEx.Equal(302, response.StatusCode);
        AssertEx.Equal("Found", response.ReasonPhrase);
        AssertEx.Equal("go", response.Body);
        AssertEx.True(headers.Any(static header => header.Name == "Location" && header.Value == "/next"));
        AssertEx.False(headers.Any(static header => string.Equals(header.Name, "Connection", StringComparison.OrdinalIgnoreCase)));
        AssertEx.False(headers.Any(static header => string.Equals(header.Name, "Keep-Alive", StringComparison.OrdinalIgnoreCase)));
        AssertEx.False(response.Headers is ProxyHeaderField[], "Generated route response headers should not expose a mutable array.");
        AssertEx.Throws<ArgumentNullException>(() => new GeneratedRouteResponse(200, "OK", null, "", null!));
        AssertEx.Throws<ArgumentNullException>(() => new GeneratedRouteResponse(200, null!, null, "", []));
        AssertEx.Throws<ArgumentNullException>(() => new GeneratedRouteResponse(200, "OK", null, null!, []));
    }

    public static void RequestContextRecordsGeneratedFailureResponse()
    {
        var context = new ProxyRequestContext("req-generated", "listener", "tcp", "127.0.0.1:50000", 7, TimeProvider.System);
        var response = ProxyGeneratedFailurePolicy.BuildFailureResponse(ProxyFailureKind.RequestPayloadTooLarge);
        context.RecordGeneratedFailureResponse(response, keepClientConnectionOpen: false);
        AssertEx.True(context.ResponseStarted);
        AssertEx.Equal(413, context.ResponseStatusCode);
        AssertEx.Equal(ProxyFailureKind.RequestPayloadTooLarge, context.FailureKind);
        AssertEx.False(context.KeepClientConnectionOpen);
    }

    public static void RequestContextRecordsGeneratedRouteResponse()
    {
        var context = new ProxyRequestContext("req-route", "listener", "tcp", "127.0.0.1:50000", 7, TimeProvider.System);
        var response = new GeneratedRouteResponse(307, "Temporary Redirect", "text/plain", "redirect", [new ProxyHeaderField("Location", "/next")]);
        context.RecordGeneratedRouteResponse(response, keepClientConnectionOpen: true);
        AssertEx.True(context.ResponseStarted);
        AssertEx.Equal(307, context.ResponseStatusCode);
        AssertEx.Equal(ProxyFailureKind.None, context.FailureKind);
        AssertEx.True(context.KeepClientConnectionOpen);
    }

    public static void RequestContextRecordsCachedResponse()
    {
        var context = new ProxyRequestContext("req-cache", "listener", "tcp", "127.0.0.1:50000", 7, TimeProvider.System);
        var response = new CachedProxyResponse(203, "Non-Authoritative Information", [new ProxyHeaderField("content-type", "text/plain")], new byte[] { 99 }, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1));
        context.RecordCachedResponse(response, keepClientConnectionOpen: true);
        AssertEx.True(context.ResponseStarted);
        AssertEx.Equal(203, context.ResponseStatusCode);
        AssertEx.Equal("cache", context.RouteAction);
        AssertEx.Equal(ProxyFailureKind.None, context.FailureKind);
        AssertEx.True(context.KeepClientConnectionOpen);
    }

    public static void RequestContextRecordsForwardingResult()
    {
        var context = new ProxyRequestContext("req-forwarding", "listener", "tcp", "127.0.0.1:50000", 7, TimeProvider.System);
        var result = ForwardingResult.Failure(responseStarted: false, responseStatusCode: 502, failureKind: ProxyFailureKind.UpstreamConnectFailed);
        context.RecordForwardingResult(result, keepClientConnectionOpen: false);
        AssertEx.False(context.ResponseStarted);
        AssertEx.Equal(502, context.ResponseStatusCode);
        AssertEx.Equal(ProxyFailureKind.UpstreamConnectFailed, context.FailureKind);
        AssertEx.False(context.KeepClientConnectionOpen);
    }

    public static void RequestContextRecordsTunnelCompletion()
    {
        var context = new ProxyRequestContext("req-tunnel", "listener", "tcp", "127.0.0.1:50000", 7, TimeProvider.System);
        var result = ForwardingResult.TunnelCompleted(101, TunnelRelayResult.Closed(bytesClientToUpstream: 11, bytesUpstreamToClient: 17, duration: TimeSpan.FromSeconds(2)));
        context.RecordTunnelCompletion((ForwardingResult.TunnelCompletedResult)result);
        AssertEx.True(context.TunnelEstablished);
        AssertEx.Equal("Closed", context.TunnelCloseReason);
        AssertEx.Equal(11L, context.TunnelBytesClientToUpstream);
        AssertEx.Equal(17L, context.TunnelBytesUpstreamToClient);
    }

    public static void RequestContextRecordsClientDisconnect()
    {
        var context = new ProxyRequestContext("req-disconnect", "listener", "tcp", "127.0.0.1:50000", 7, TimeProvider.System);
        context.RecordClientDisconnect();
        AssertEx.Equal(ProxyFailureKind.ClientDisconnected, context.FailureKind);
    }

    public static void RequestContextRecordsUpgradeRequest()
    {
        var context = new ProxyRequestContext("req-upgrade", "listener", "tcp", "127.0.0.1:50000", 7, TimeProvider.System);
        context.RecordUpgradeRequest();
        AssertEx.True(context.IsUpgrade);
    }

    public static void RequestContextRecordsClientConnectionClose()
    {
        var context = new ProxyRequestContext("req-close", "listener", "tcp", "127.0.0.1:50000", 7, TimeProvider.System);
        var response = new GeneratedRouteResponse(204, "No Content", null, string.Empty, []);
        context.RecordGeneratedRouteResponse(response, keepClientConnectionOpen: true);
        context.RecordClientConnectionClose();
        AssertEx.False(context.KeepClientConnectionOpen);
    }

    public static void RouteDiagnosticsStatusNamesEnabledAvailability()
    {
        var status = RouteDiagnosticsStatus.Enabled;
        AssertEx.True(status.Available);
    }

    public static void DryRunRedactsSensitiveHeaders()
    {
        var service = CreateRouteService(BaseOptions([ProxyRoute("private", "diag.test", "/private", cache: CachePolicy())]), out _, out _);
        var result = service.Explain(new RouteMatchDryRunRequest("http", "diag.test", 8080, "GET", "/private", "", new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Authorization"] = "Bearer secret-token" }, null, null));
        AssertEx.Equal("authorization", result.Cache.Reason);
        AssertEx.True(result.Findings.Any(static finding => finding.Code == "sensitive_header_redacted"));
        AssertEx.False(result.Findings is List<RouteMatchDryRunFinding>, "Route diagnostics result findings should not expose a mutable list.");
        AssertEx.False(result.ToString()!.Contains("secret-token", StringComparison.Ordinal));
    }

    public static void RouteDiagnosticsServiceShapesNoListenerResult()
    {
        var metrics = new FixedRouteDiagnosticsMetricsSink();
        var service = CreateBllRouteService(new FixedRouteDiagnosticsConfigurationSnapshot([RouteDiagnosticsListener("secure", "https", 8443, RuntimeListenerProtocols.Http1AndHttp2, http3EnabledForTraffic: false)], [RouteDiagnosticsRoute("api", "diag.test", "/api")]), metricsSink: metrics);
        var result = service.Explain(new RouteMatchDryRunRequest("http", "diag.test", 8080, "GET", "/api", "", NoHeaders(), null, null));
        var noListener = NoMatchingListener(result);
        AssertEx.Equal("no_matching_listener", noListener.NoMatchReason);
        AssertEx.Equal("/api", noListener.OriginalTarget);
        AssertEx.Equal("no_route", result.Cache.Reason);
        AssertEx.True(result.Findings.Any(static finding => finding.Code == "no_matching_listener"));
        AssertEx.Equal("no_matching_listener", metrics.LastFailureReason!);
    }

    public static void RouteDiagnosticsServiceSelectsListenerBeforePolicyExplanation()
    {
        var actionPolicy = new FixedRouteDiagnosticsActionPolicy(ProxyRouteDiagnosticsActionDecision.Proxy);
        var metrics = new FixedRouteDiagnosticsMetricsSink();
        var service = CreateBllRouteService(new FixedRouteDiagnosticsConfigurationSnapshot([RouteDiagnosticsListener("web", "http", 8080, RuntimeListenerProtocols.Http1, http3EnabledForTraffic: false), RouteDiagnosticsListener("secure", "https", 8443, RuntimeListenerProtocols.Http1AndHttp2AndHttp3, http3EnabledForTraffic: true)], [RouteDiagnosticsRoute("api", "diag.test", "/api")]), actionPolicy: actionPolicy, metricsSink: metrics);
        var result = service.Explain(new RouteMatchDryRunRequest("https", "diag.test", 8443, "GET", "/api/users", "", NoHeaders(), null, "secure", "http3"));
        var matched = Matched(result);
        AssertEx.Equal("secure", matched.Listener.Name);
        AssertEx.Equal("secure", AssertEx.NotNull(actionPolicy.LastListener).Name);
        AssertEx.Equal("/api/users", AssertEx.NotNull(actionPolicy.LastRequestHead).Target);
        AssertEx.Equal(null, metrics.LastFailureReason);
    }

    public static void RouteDiagnosticsServiceRedactsSensitiveHeadersBeforeAdapters()
    {
        var actionPolicy = new FixedRouteDiagnosticsActionPolicy(ProxyRouteDiagnosticsActionDecision.Proxy);
        var service = CreateBllRouteService(new FixedRouteDiagnosticsConfigurationSnapshot([RouteDiagnosticsListener("web", "http", 8080, RuntimeListenerProtocols.Http1, http3EnabledForTraffic: false)], [RouteDiagnosticsRoute("private", "diag.test", "/private", cacheEnabled: true)]), actionPolicy: actionPolicy);
        var result = service.Explain(new RouteMatchDryRunRequest("http", "diag.test", 8080, "GET", "/private", "", new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Authorization"] = "Bearer secret-token" }, null, null));
        var requestHead = AssertEx.NotNull(actionPolicy.LastRequestHead);
        var authorization = requestHead.Headers.First(static header => string.Equals(header.Name, "Authorization", StringComparison.OrdinalIgnoreCase));
        AssertEx.Equal("redacted", authorization.Value);
        AssertEx.Equal("authorization", result.Cache.Reason);
        AssertEx.True(result.Findings.Any(static finding => finding.Code == "sensitive_header_redacted"));
        AssertEx.False(result.ToString()!.Contains("secret-token", StringComparison.Ordinal));
    }

    public static void LintDetectsRouteShadowingAndBroadCatchAll()
    {
        var service = CreateLintService(BaseOptions([ProxyRoute("catch-all", "*", "/"), ProxyRoute("api", "diag.test", "/api")]));
        var result = service.LintActive();
        AssertFinding(result, "route_shadowed", "warning");
        AssertFinding(result, "broad_catch_all_before_specific", "warning");
    }

    public static void LintDetectsCanonicalRedirectLoop()
    {
        var route = new ProxyRouteOptions
        {
            Name = "loop",
            SiteName = "diag",
            Host = "diag.test",
            PathPrefix = "/",
            Action = "proxy",
            CanonicalHost = new ProxyCanonicalHostOptions
            {
                Enabled = true,
                TargetHost = "diag.test"
            },
            Upstreams = [new UpstreamOptions
            {
                Name = "local",
                Address = "127.0.0.1",
                Port = 5000
            }

            ]
        };
        var service = CreateLintService(BaseOptions([route]));
        var result = service.LintActive();
        AssertFinding(result, "canonical_host_loop", "warning");
    }

    public static void LintDetectsHttpsRedirectWithoutHttpsListener()
    {
        var route = new ProxyRouteOptions
        {
            Name = "redirect",
            SiteName = "diag",
            Host = "diag.test",
            PathPrefix = "/",
            Action = "proxy",
            HttpsRedirect = new ProxyHttpsRedirectOptions
            {
                Enabled = true
            },
            Upstreams = [new UpstreamOptions
            {
                Name = "local",
                Address = "127.0.0.1",
                Port = 5000
            }

            ]
        };
        var service = CreateLintService(BaseOptions([route]));
        var result = service.LintActive();
        AssertFinding(result, "https_redirect_without_https_listener", "warning");
    }

    public static void LintWarnsAboutUnsafeUpstreamTlsValidation()
    {
        var route = ProxyRoute("api", "diag.test", "/api", scheme: "https", upstreamTls: new UpstreamTlsOptions { ValidateCertificate = false });
        var service = CreateLintService(BaseOptions([route]));
        var result = service.LintActive();
        AssertFinding(result, "unsafe_upstream_tls_validation_disabled", "warning");
    }

    public static void LintDoesNotReportResolvedUpstreamHttp3PoolingLimitation()
    {
        var route = ProxyRoute("h3", "diag.test", "/h3", scheme: "https", upstreamProtocol: RuntimeUpstreamProtocol.Http3);
        var service = CreateLintService(BaseOptions([route]));
        var result = service.LintActive();
        AssertNoFinding(result, "upstream_http3_one_request_per_connection");
    }

    public static void ConfigLintSnapshotMapperReadsRuntimeSourceWithoutConfigurationSnapshot()
    {
        var snapshot = Snapshot(BaseOptions([ProxyRoute("api", "diag.test", "/api")]));
        var sourceFiles = new List<string>
        {
            "site.json"
        };
        var adminUrls = new List<string>
        {
            AdminBindPolicy.DefaultAdminUrl
        };
        var listeners = new List<ProxyConfigLintRuntimeListenerSource>
        {
            new(snapshot.Listeners[0].Name, snapshot.Listeners[0].Address, snapshot.Listeners[0].Port, snapshot.Listeners[0].Enabled, snapshot.Listeners[0].Transport.ToString(), snapshot.Listeners[0].Http3.Configured, snapshot.Listeners[0].Http3.EnabledForTraffic, snapshot.Listeners[0].Http3.DisabledReason, snapshot.Listeners[0].Http3.EnablementLevel, false, snapshot.Listeners[0].QuicIdentity?.Key)
        };
        var cacheVaryByHeaders = new List<string>
        {
            "X-Tenant"
        };
        var retryMethods = new List<string>
        {
            "GET"
        };
        var upstreams = new List<ProxyConfigLintRuntimeUpstreamSource>
        {
            new("local", "http", RuntimeUpstreamProtocol.Http1, true, false)
        };
        var route = new ProxyConfigLintRuntimeRouteSource("api", "diag", "diag.test", "/api", RuntimeRouteAction.Proxy.ToString(), false, false, "", true, cacheVaryByHeaders, true, retryMethods, false, upstreams, "");
        var routes = new List<ProxyConfigLintRuntimeRouteSource>
        {
            route
        };
        var http3Support = new Http3SupportConfigurationSource([new Http3SupportListenerSource(snapshot.Listeners[0].Http3.Configured, snapshot.Listeners[0].Http3.EnabledForTraffic, snapshot.Listeners[0].Http3.EnablementLevel, false, 86400, snapshot.Listeners[0].QuicIdentity?.Key)], UpstreamHttp3Configured: false);
        var source = new ProxyConfigLintRuntimeConfigurationSource(sourceFiles, adminUrls, snapshot.AdminSecurity.RequireAuthentication, snapshot.Metrics.PublicMetricsEnabled, http3Support, listeners, routes);
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigLintConfigurationSnapshotMapper.ToLintSnapshot(null!, TestHttp3PlatformSupport.Supported));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigLintConfigurationSnapshotMapper.ToLintSnapshot(source, null!));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigLintRuntimeConfigurationSource(sourceFiles, adminUrls, snapshot.AdminSecurity.RequireAuthentication, snapshot.Metrics.PublicMetricsEnabled, http3Support, [null!], routes));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigLintRuntimeConfigurationSource(sourceFiles, adminUrls, snapshot.AdminSecurity.RequireAuthentication, snapshot.Metrics.PublicMetricsEnabled, http3Support, listeners, [null!]));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigLintRuntimeRouteSource("api", "diag", "diag.test", "/api", RuntimeRouteAction.Proxy.ToString(), false, false, "", true, cacheVaryByHeaders, true, retryMethods, false, [null!], ""));
        var mappedSourceFiles = new List<string>
        {
            "mapped.json"
        };
        var mappedAdminUrls = new List<string>
        {
            AdminBindPolicy.DefaultAdminUrl
        };
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigLintRuntimeConfigurationSourceMapper.FromSources(mappedSourceFiles, mappedAdminUrls, snapshot.AdminSecurity.RequireAuthentication, snapshot.Metrics.PublicMetricsEnabled, [null!], snapshot.Routes));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigLintRuntimeConfigurationSourceMapper.FromSources(mappedSourceFiles, mappedAdminUrls, snapshot.AdminSecurity.RequireAuthentication, snapshot.Metrics.PublicMetricsEnabled, snapshot.Listeners, [null!]));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigLintRuntimeConfigurationSourceMapper.FromSources(mappedSourceFiles, mappedAdminUrls, snapshot.AdminSecurity.RequireAuthentication, snapshot.Metrics.PublicMetricsEnabled, snapshot.Listeners, [snapshot.Routes[0].WithUpstreams([null!])]));
        var mappedSource = ProxyConfigLintRuntimeConfigurationSourceMapper.FromSources(mappedSourceFiles.Select(static sourceFile => sourceFile), mappedAdminUrls.Select(static adminUrl => adminUrl), snapshot.AdminSecurity.RequireAuthentication, snapshot.Metrics.PublicMetricsEnabled, snapshot.Listeners.Select(static listener => listener), snapshot.Routes.Select(static route => route));
        var lintSnapshot = ProxyConfigLintConfigurationSnapshotMapper.ToLintSnapshot(source, TestHttp3PlatformSupport.Supported);
        mappedSourceFiles.Clear();
        mappedAdminUrls.Clear();
        sourceFiles[0] = "replacement.json";
        adminUrls[0] = "http://0.0.0.0:9999";
        listeners.Clear();
        routes.Clear();
        cacheVaryByHeaders[0] = "X-Replacement";
        retryMethods[0] = "POST";
        upstreams[0] = new ProxyConfigLintRuntimeUpstreamSource("replacement", "http", RuntimeUpstreamProtocol.Http1, true, false);
        sourceFiles.Clear();
        adminUrls.Clear();
        cacheVaryByHeaders.Clear();
        retryMethods.Clear();
        upstreams.Clear();
        AssertEx.Equal(1, lintSnapshot.SourceFiles.Count);
        AssertEx.Equal("site.json", lintSnapshot.SourceFiles[0]);
        AssertEx.Equal("site.json", source.SourceFiles[0]);
        AssertEx.Equal(AdminBindPolicy.DefaultAdminUrl, source.AdminUrls[0]);
        AssertEx.Equal(1, source.Listeners.Count);
        AssertEx.Equal(1, source.Routes.Count);
        AssertEx.Equal("mapped.json", mappedSource.SourceFiles[0]);
        AssertEx.Equal(AdminBindPolicy.DefaultAdminUrl, mappedSource.AdminUrls[0]);
        AssertEx.Equal(1, mappedSource.Listeners.Count);
        AssertEx.Equal(1, mappedSource.Routes.Count);
        AssertEx.False(mappedSource.SourceFiles is string[], "Mapped config lint runtime source files should not expose a mutable array.");
        AssertEx.False(mappedSource.Listeners is ProxyConfigLintRuntimeListenerSource[], "Mapped config lint runtime source listeners should not expose a mutable array.");
        AssertEx.False(mappedSource.Routes is ProxyConfigLintRuntimeRouteSource[], "Mapped config lint runtime source routes should not expose a mutable array.");
        AssertEx.Equal("X-Tenant", source.Routes[0].CacheVaryByHeaders[0]);
        AssertEx.Equal("GET", source.Routes[0].RetryMethods[0]);
        AssertEx.Equal("local", source.Routes[0].Upstreams[0].Name);
        AssertEx.Equal(snapshot.AdminSecurity.RequireAuthentication, lintSnapshot.AdminSecurity.RequireAuthentication);
        AssertEx.Equal(AdminBindPolicy.DefaultAdminUrl, lintSnapshot.AdminSecurity.Urls[0]);
        AssertEx.Equal(snapshot.Metrics.PublicMetricsEnabled, lintSnapshot.Metrics.PublicMetricsEnabled);
        AssertEx.Equal(1, lintSnapshot.Listeners.Count);
        AssertEx.Equal(1, lintSnapshot.Routes.Count);
        AssertEx.Equal("api", lintSnapshot.Routes[0].Name);
        AssertEx.Equal("X-Tenant", lintSnapshot.Routes[0].CacheVaryByHeaders[0]);
        AssertEx.Equal("GET", lintSnapshot.Routes[0].RetryMethods[0]);
        AssertEx.Equal("local", lintSnapshot.Routes[0].Upstreams[0].Name);
        AssertEx.False(source.SourceFiles is string[], "Config lint runtime source files should not expose a mutable array.");
        AssertEx.False(source.Listeners is ProxyConfigLintRuntimeListenerSource[], "Config lint runtime source listeners should not expose a mutable array.");
        AssertEx.False(source.Routes is ProxyConfigLintRuntimeRouteSource[], "Config lint runtime source routes should not expose a mutable array.");
        AssertEx.False(source.Routes[0].CacheVaryByHeaders is string[], "Config lint runtime source vary headers should not expose a mutable array.");
        AssertEx.False(source.Routes[0].Upstreams is ProxyConfigLintRuntimeUpstreamSource[], "Config lint runtime source upstreams should not expose a mutable array.");
        AssertEx.False(lintSnapshot.SourceFiles is string[], "Config lint snapshot source files should not expose a mutable array.");
        AssertEx.False(lintSnapshot.AdminSecurity.Urls is string[], "Config lint admin URLs should not expose a mutable array.");
        AssertEx.False(lintSnapshot.Routes is ProxyConfigLintRoute[], "Config lint routes should not expose a mutable array.");
        AssertEx.False(lintSnapshot.Routes[0].CacheVaryByHeaders is string[], "Config lint route vary headers should not expose a mutable array.");
        AssertEx.False(lintSnapshot.Routes[0].Upstreams is ProxyConfigLintUpstream[], "Config lint route upstreams should not expose a mutable array.");
    }

    public static void ConfigLintRuntimeListenerStateMapperReadsListenerStatuses()
    {
        var listeners = new List<ProxyListenerStatus>
        {
            ListenerStatus("main|tcp", "tcp", ProxyListenerState.Active),
            ListenerStatus("main|quic", "quic", ProxyListenerState.Failed)
        };
        var states = ProxyConfigLintRuntimeListenerStateMapper.FromListenerStatuses(listeners);
        listeners.Clear();
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigLintRuntimeListenerStateMapper.FromListenerStatuses(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigLintRuntimeListenerStateMapper.FromListenerStatuses([null!]));
        AssertEx.Equal(2, states.Count);
        AssertEx.Equal("main|tcp", states[0].Identity);
        AssertEx.Equal("tcp", states[0].Kind);
        AssertEx.True(states[0].Active);
        AssertEx.Equal("main|quic", states[1].Identity);
        AssertEx.Equal("quic", states[1].Kind);
        AssertEx.False(states[1].Active);
        AssertEx.False(states is ProxyConfigLintRuntimeListenerState[], "Config lint runtime listener states should not expose a mutable array.");
    }

    public static void ConfigLintRuntimeSourceFactsRejectInvalidValues()
    {
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigLintRuntimeListenerSource(" ", "127.0.0.1", 8080, true, "Http", false, false, "not_configured", "disabled", false, null));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyConfigLintRuntimeListenerSource("web", "127.0.0.1", -1, true, "Http", false, false, "not_configured", "disabled", false, null));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigLintRuntimeRouteSource("api", "diag", "*", "", "Proxy", false, false, "", false, [], false, [], false, [], ""));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigLintRuntimeRouteSource("api", "diag", "*", "/", "Proxy", false, false, "", false, [], false, [], false, [], null!));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigLintRuntimeUpstreamSource("local", " ", RuntimeUpstreamProtocol.Http1, true, false));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigLintRuntimeUpstreamSource("local", "http", "", true, false));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigLintRuntimeListenerState(" ", "tcp", true));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigLintRuntimeListenerState("main|tcp", "", true));
    }

    public static void RouteDiagnosticsRuntimeRouteCopiesPolicyMethodLists()
    {
        var cacheMethods = new List<string>
        {
            "GET"
        };
        var retryMethods = new List<string>
        {
            "POST"
        };
        var upstreams = new List<RuntimeUpstream>
        {
            new("api", "primary", "http", RuntimeUpstreamProtocol.Http1, "127.0.0.1", 18080, 1, RuntimeUpstreamTlsOptions.Default)
        };
        var route = Route(cacheMethods, retryMethods, upstreams);
        var store = new ProxyConfigurationStore();
        store.Replace(RuntimeSnapshot([route]));
        var source = new ProxyRouteDiagnosticsConfigurationSource(store);
        var readResult = source.Read();
        cacheMethods.Clear();
        retryMethods.Clear();
        upstreams.Clear();
        AssertEx.True(readResult is ProxyRouteDiagnosticsConfigurationReadResult.AvailableResult);
        var snapshot = ((ProxyRouteDiagnosticsConfigurationReadResult.AvailableResult)readResult).Snapshot;
        var diagnosticRoute = snapshot.Routes[0];
        AssertEx.False(snapshot.Listeners is IProxyRouteDiagnosticsListener[], "Route diagnostics listeners should not expose a mutable array.");
        AssertEx.False(snapshot.Routes is IProxyRouteDiagnosticsRoute[], "Route diagnostics routes should not expose a mutable array.");
        AssertEx.Equal("primary", diagnosticRoute.Upstreams[0].Name);
        AssertEx.False(diagnosticRoute.Upstreams is IProxyRouteDiagnosticsUpstream[], "Route diagnostics upstreams should not expose a mutable array.");
        AssertEx.Equal("GET", diagnosticRoute.CacheMethods[0]);
        AssertEx.False(diagnosticRoute.CacheMethods is string[], "Route diagnostics cache methods should not expose a mutable array.");
        AssertEx.Equal("POST", diagnosticRoute.RetryMethods[0]);
        AssertEx.False(diagnosticRoute.RetryMethods is string[], "Route diagnostics retry methods should not expose a mutable array.");
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRuntimeRoute(Route([null!], ["POST"], [new("api", "primary", "http", RuntimeUpstreamProtocol.Http1, "127.0.0.1", 18080, 1, RuntimeUpstreamTlsOptions.Default)])));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRuntimeRoute(Route(["GET"], [null!], [new("api", "primary", "http", RuntimeUpstreamProtocol.Http1, "127.0.0.1", 18080, 1, RuntimeUpstreamTlsOptions.Default)])));
        static RuntimeRoute Route(IEnumerable<string> cacheMethods, IEnumerable<string> retryMethods, IReadOnlyList<RuntimeUpstream> upstreams)
        {
            return new RuntimeRoute("api", "diag.test", "/api", RuntimeRouteAction.Proxy, "round-robin", new RuntimeHealthCheckOptions(false, "/health", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1, 1), upstreams, new RuntimeHttpsRedirectPolicy(false, 308, null), new RuntimeCanonicalHostPolicy(false, "", 308), RuntimeHeaderPolicy.Empty, new RuntimePathRewritePolicy("", "", ""), new RuntimeRedirectPolicy(308, "", "", true), new RuntimeStaticResponse(200, "text/plain; charset=utf-8", ""), new RuntimeMaintenancePolicy(false, null, "text/plain; charset=utf-8", "Service Unavailable"), new RuntimeCachePolicy(true, 1024, 4096, TimeSpan.FromSeconds(60), true, [], [200], cacheMethods), new RuntimeRouteResolvedOptions(104857600, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), true), SiteName: "", Retry: new RuntimeRetryPolicy(true, 2, null, true, false, [], retryMethods, TimeSpan.Zero));
        }
    }

    public static void RouteDiagnosticsRuntimeConfigurationMapperReadsRuntimeFactsWithoutSnapshot()
    {
        var listener = new RuntimeListener("main", "127.0.0.1", 8080, true, RuntimeListenerTransport.Http, null, [], 512, 32768, 32768, 8192, 8192);
        var route = new RuntimeRoute("api", "diag.test", "/api", RuntimeRouteAction.Proxy, "round-robin", new RuntimeHealthCheckOptions(false, "/health", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1, 1), [], new RuntimeHttpsRedirectPolicy(false, 308, null), new RuntimeCanonicalHostPolicy(false, "", 308), RuntimeHeaderPolicy.Empty, new RuntimePathRewritePolicy("", "", ""), new RuntimeRedirectPolicy(308, "", "", true), new RuntimeStaticResponse(200, "text/plain; charset=utf-8", ""), new RuntimeMaintenancePolicy(false, null, "text/plain; charset=utf-8", "Service Unavailable"), RuntimeCachePolicy.Disabled, new RuntimeRouteResolvedOptions(104857600, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), true));
        var diagnosticsSnapshot = ProxyRouteDiagnosticsRuntimeConfigurationSnapshotMapper.FromSources(new[] { listener }.Select(static source => source), new[] { route }.Select(static source => source));
        AssertEx.Equal(1, diagnosticsSnapshot.Listeners.Count);
        AssertEx.Equal("main", diagnosticsSnapshot.Listeners[0].Name);
        AssertEx.Equal("http", diagnosticsSnapshot.Listeners[0].Transport);
        AssertEx.Equal(1, diagnosticsSnapshot.Routes.Count);
        AssertEx.Equal("api", diagnosticsSnapshot.Routes[0].Name);
        AssertEx.Equal("diag.test", diagnosticsSnapshot.Routes[0].Host);
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteDiagnosticsRuntimeConfigurationSnapshotMapper.FromSources(null!, [route]));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteDiagnosticsRuntimeConfigurationSnapshotMapper.FromSources([listener], null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteDiagnosticsRuntimeConfigurationSnapshotMapper.FromSources([null!], [route]));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteDiagnosticsRuntimeConfigurationSnapshotMapper.FromSources([listener], [null!]));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteDiagnosticsRuntimeConfigurationSnapshotMapper.FromSources([listener], [route.WithUpstreams([null!])]));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRuntimeListener(null!));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRuntimeRoute(null!));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRuntimeUpstream(null!));
        var listenerSources = new List<RuntimeListener>
        {
            listener
        };
        var routeSources = new List<RuntimeRoute>
        {
            route
        };
        var directSnapshot = ProxyRouteDiagnosticsRuntimeConfigurationSnapshotMapper.FromSources(listenerSources.Select(static source => source), routeSources.Select(static source => source));
        listenerSources.Clear();
        routeSources.Clear();
        AssertEx.Equal(1, directSnapshot.Listeners.Count);
        AssertEx.Equal("main", directSnapshot.Listeners[0].Name);
        AssertEx.Equal(1, directSnapshot.Routes.Count);
        AssertEx.Equal("api", directSnapshot.Routes[0].Name);
        AssertEx.False(directSnapshot.Listeners is IProxyRouteDiagnosticsListener[], "Mapped route diagnostics listeners should not expose a mutable array.");
        AssertEx.False(directSnapshot.Routes is IProxyRouteDiagnosticsRoute[], "Mapped route diagnostics routes should not expose a mutable array.");
    }

    public static void LintHandlesJsonAndYamlSubmittedConfigWithoutApplying()
    {
        var service = CreateLintService(BaseOptions([ProxyRoute("active", "active.test", "/")]), out var store);
        var json = SubmittedSiteJson("jsonsite", "json.test");
        var yaml = """
        name: yamlsite
        listeners:
          - name: web
            address: 127.0.0.1
            port: 8080
            transport: http
        host: yaml.test
        routes:
          - name: catch-all
            pathPrefix: /
            action: proxy
            upstreams:
              - name: local
                address: 127.0.0.1
                port: 5000
          - name: api
            pathPrefix: /api
            action: proxy
            upstreams:
              - name: local
                address: 127.0.0.1
                port: 5000
        """;
        var jsonResult = service.LintSubmitted(new ConfigLintRequest("json", json));
        var yamlResult = service.LintSubmitted(new ConfigLintRequest("yaml", yaml));
        AssertAccepted(jsonResult, string.Join("; ", jsonResult.Findings.Select(static finding => finding.Message)));
        AssertFinding(yamlResult, "route_shadowed", "warning");
        AssertEx.Equal("active", store.Snapshot.Routes[0].Name);
    }

    public static void LintOutputHasStableCodesAndSeverities()
    {
        var service = CreateLintService(BaseOptions([ProxyRoute("catch-all", "*", "/"), ProxyRoute("api", "diag.test", "/api")]));
        var result = service.LintActive();
        var finding = result.Findings.First(static item => item.Code == "route_shadowed");
        AssertEx.Equal("warning", finding.Severity);
        AssertEx.True(result.Summary.Warning > 0);
    }

    public static void ConfigLintFactsRejectInvalidValues()
    {
        AssertEx.Throws<ArgumentException>(() => new ConfigLintFinding(null!, "route_shadowed", "Route is shadowed.", null, null, null));
        AssertEx.Throws<ArgumentException>(() => new ConfigLintFinding("warning", " ", "Route is shadowed.", null, null, null));
        AssertEx.Throws<ArgumentException>(() => new ConfigLintFinding("warning", "route_shadowed", "", null, null, null));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ConfigLintSummary(-1, 0, 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ConfigLintSummary(0, -1, 0));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ConfigLintSummary(0, 0, -1));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigLintListener(" ", "127.0.0.1", 8080, true, "Http", false, false, "not_configured", "disabled", false, null));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyConfigLintListener("web", "127.0.0.1", -1, true, "Http", false, false, "not_configured", "disabled", false, null));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigLintListener("web", "127.0.0.1", 8080, true, "", false, false, "not_configured", "disabled", false, null));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigLintRoute(" ", "diag", "*", "/", "Proxy", false, false, "", false, [], false, [], false, [], ""));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigLintRoute("api", "diag", null!, "/", "Proxy", false, false, "", false, [], false, [], false, [], ""));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigLintUpstream(" ", "http", RuntimeUpstreamProtocol.Http1, true, false));
        AssertEx.Throws<ArgumentException>(() => new ProxyConfigLintUpstream("local", "http", "", true, false));
    }

    public static void ConfigLintServiceShapesActiveSourceFindings()
    {
        var metrics = new FixedConfigLintMetricsSink();
        var service = new ConfigLintService(new FixedConfigLintActiveConfigurationSource(LintSnapshot("active.json")), new FixedConfigLintSubmittedConfigurationSource(null), new FixedConfigLintRuntimeStateSource([]), metrics, new ProxyConfigLintSourceNameFormatter(), new ProxyAdminUrlPolicy(), TimeProvider.System);
        var result = service.LintActive();
        AssertAccepted(result);
        AssertFinding(result, "route_shadowed", "warning");
        AssertEx.Equal("active.json", result.Findings.First(static finding => finding.Code == "route_shadowed").Source);
        AssertEx.Equal(result.Summary, service.LastActiveStatus.LastActiveLintSummary);
        AssertEx.Equal(result.Findings.Count, metrics.LastFindings.Count);
        var missingResult = new ConfigLintService(new FixedConfigLintActiveConfigurationSource(null), new FixedConfigLintSubmittedConfigurationSource(null), new FixedConfigLintRuntimeStateSource([]), metrics, new ProxyConfigLintSourceNameFormatter(), new ProxyAdminUrlPolicy(), TimeProvider.System).LintActive();
        var missingFinding = missingResult.Findings.First(static finding => finding.Code == "no_active_config");
        AssertEx.Equal("Load a valid config before linting the active configuration source.", missingFinding.SuggestedFix);
    }

    public static void ConfigLintStatusNamesEmptyAndCompletedStates()
    {
        var lintedAtUtc = DateTimeOffset.UnixEpoch.AddHours(5);
        var summary = new ConfigLintSummary(Info: 1, Warning: 2, Error: 3);
        var empty = ConfigLintStatus.Empty;
        var completed = ConfigLintStatus.Completed(lintedAtUtc, summary);
        AssertEx.True(empty.Available);
        AssertEx.Equal<DateTimeOffset?>(null, empty.LastActiveLintAtUtc);
        AssertEx.Equal<ConfigLintSummary?>(null, empty.LastActiveLintSummary);
        AssertEx.True(completed.Available);
        AssertEx.Equal(lintedAtUtc, completed.LastActiveLintAtUtc!.Value);
        AssertEx.Equal(summary, completed.LastActiveLintSummary);
    }

    public static void ConfigLintSubmittedConfigurationResultNamesSourceOutcomes()
    {
        var snapshot = LintSnapshot("submitted.json");
        var validationError = ProxyConfigurationFileError.ForPath("lint-input", "Proxy:Routes:0:Name is required.");
        var loaded = ProxyConfigLintSubmittedConfigurationResult.Loaded(snapshot, [validationError]);
        var failed = ProxyConfigLintSubmittedConfigurationResult.Failed(ProxyConfigLintSubmittedConfigurationFailureKind.JsonParseError, "bad json");
        var empty = ProxyConfigLintSubmittedConfigurationResult.Empty();
        AssertEx.True(loaded is ProxyConfigLintSubmittedConfigurationResult.LoadedResult);
        var loadedResult = (ProxyConfigLintSubmittedConfigurationResult.LoadedResult)loaded;
        AssertEx.Equal(snapshot, loadedResult.Snapshot);
        AssertEx.Equal(validationError, loadedResult.ValidationErrors[0]);
        AssertEx.True(failed is ProxyConfigLintSubmittedConfigurationResult.FailedResult);
        var failure = ((ProxyConfigLintSubmittedConfigurationResult.FailedResult)failed).Failure;
        AssertEx.Equal(ProxyConfigLintSubmittedConfigurationFailureKind.JsonParseError, failure.Kind);
        AssertEx.Equal("bad json", failure.Message);
        AssertEx.True(empty is ProxyConfigLintSubmittedConfigurationResult.EmptyResult);
    }

    public static void ConfigLintResultsCopyInputCollections()
    {
        var lintedAtUtc = DateTimeOffset.UnixEpoch.AddHours(6);
        var findings = new List<ConfigLintFinding>
        {
            new("warning", "route_shadowed", "Route is shadowed.", "lint-input", "routes[1]", "Move it earlier.")
        };
        var validationErrors = new List<ProxyConfigurationFileError>
        {
            ProxyConfigurationFileError.ForPath("lint-input", "Proxy:Routes:0:Name is required.")
        };
        var snapshot = LintSnapshot("submitted.json");
        var result = ConfigLintResult.Completed(lintedAtUtc, findings, validationErrors);
        var submitted = ProxyConfigLintSubmittedConfigurationResult.Loaded(snapshot, validationErrors);
        findings.Clear();
        validationErrors.Clear();
        AssertEx.Equal("route_shadowed", result.Findings[0].Code);
        AssertEx.Equal("lint-input", result.ValidationErrors[0].Path);
        AssertEx.False(result.Findings is ConfigLintFinding[], "Config lint findings should not expose a mutable array.");
        AssertEx.False(result.ValidationErrors is ProxyConfigurationFileError[], "Config lint validation errors should not expose a mutable array.");
        AssertEx.True(submitted is ProxyConfigLintSubmittedConfigurationResult.LoadedResult);
        var loaded = (ProxyConfigLintSubmittedConfigurationResult.LoadedResult)submitted;
        AssertEx.Equal("lint-input", loaded.ValidationErrors[0].Path);
        AssertEx.False(loaded.ValidationErrors is ProxyConfigurationFileError[], "Submitted config lint validation errors should not expose a mutable array.");
        AssertEx.Throws<ArgumentNullException>(() => ConfigLintResult.Completed(lintedAtUtc, [null!], []));
        AssertEx.Throws<ArgumentNullException>(() => ConfigLintResult.Completed(lintedAtUtc, [], [null!]));
        AssertEx.Throws<ArgumentNullException>(() => ProxyConfigLintSubmittedConfigurationResult.Loaded(snapshot, [null!]));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigLintConfigurationSnapshot([null!], new ProxyConfigLintAdminSecurity([AdminBindPolicy.DefaultAdminUrl], true), new ProxyConfigLintMetricsOptions(false), true, [], []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyConfigLintConfigurationSnapshot(["submitted.json"], new ProxyConfigLintAdminSecurity([AdminBindPolicy.DefaultAdminUrl], true), new ProxyConfigLintMetricsOptions(false), true, [], [null!]));
    }

    public static void ConfigLintServiceShapesSubmittedSourceFindings()
    {
        var source = new FixedConfigLintSubmittedConfigurationSource(ProxyConfigLintSubmittedConfigurationResult.Loaded(LintSnapshot("submitted.json"), [ProxyConfigurationFileError.ForPath("lint-input", "Proxy:Routes:0:Name is required.")]));
        var service = new ConfigLintService(new FixedConfigLintActiveConfigurationSource(null), source, new FixedConfigLintRuntimeStateSource([]), new FixedConfigLintMetricsSink(), new ProxyConfigLintSourceNameFormatter(), new ProxyAdminUrlPolicy(), TimeProvider.System);
        var result = service.LintSubmitted(new ConfigLintRequest("yml", "submitted"));
        AssertRejected(result);
        AssertEx.Equal(ProxyConfigurationNormalizeFormat.Yaml, source.LastFormat);
        AssertFinding(result, "validation_error", "error");
        AssertFinding(result, "route_shadowed", "warning");
        AssertEx.Equal("lint-input", result.Findings.First(static finding => finding.Code == "route_shadowed").Source);
        AssertEx.Equal("lint-input", result.ValidationErrors[0].Path);
    }

    public static void ConfigLintRejectsMissingSubmittedRequestBody()
    {
        var service = CreateLintService(BaseOptions([ProxyRoute("active", "active.test", "/")]));
        var result = service.LintSubmitted(null);
        AssertRejected(result);
        AssertFinding(result, "missing_request", "error");
        AssertEx.True(result.Findings[0].Message.Contains("request body is required", StringComparison.Ordinal));
    }

    public static void ConfigLintSubmittedRequestReaderAcceptsYamlInput()
    {
        var decision = ConfigLintSubmittedRequestReader.Read(new ConfigLintRequest("yml", "submitted"));
        AssertEx.True(decision is ConfigLintSubmittedRequestDecision.AcceptedDecision);
        var accepted = (ConfigLintSubmittedRequestDecision.AcceptedDecision)decision;
        AssertEx.Equal("submitted", accepted.Input.Text);
        AssertEx.Equal(ProxyConfigurationNormalizeFormat.Yaml, accepted.Input.Format);
    }

    public static void ConfigLintSubmittedRequestDecisionRejectsInvalidFacts()
    {
        AssertEx.Throws<ArgumentException>(() => new ConfigLintSubmittedRequestInput(" ", ProxyConfigurationNormalizeFormat.Json));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ConfigLintSubmittedRequestInput("submitted", (ProxyConfigurationNormalizeFormat)99));
        AssertEx.Throws<ArgumentNullException>(() => ConfigLintSubmittedRequestDecision.Accepted(null!));
        AssertEx.Throws<ArgumentNullException>(() => new ConfigLintSubmittedRequestDecision.AcceptedDecision(null!));
        AssertEx.Throws<ArgumentNullException>(() => ConfigLintSubmittedRequestDecision.Rejected(null!));
        AssertEx.Throws<ArgumentNullException>(() => new ConfigLintSubmittedRequestDecision.RejectedDecision(null!));
    }

    public static void ConfigLintSubmittedRequestReaderRejectsMissingRequest()
    {
        var decision = ConfigLintSubmittedRequestReader.Read(null);
        AssertEx.True(decision is ConfigLintSubmittedRequestDecision.RejectedDecision);
        var rejected = (ConfigLintSubmittedRequestDecision.RejectedDecision)decision;
        AssertEx.Equal("missing_request", rejected.Failure.Code);
        AssertEx.Equal("error", rejected.Failure.Severity);
    }

    public static void ConfigLintControllerRejectsMissingSubmittedRequestBody()
    {
        var service = CreateLintService(BaseOptions([ProxyRoute("active", "active.test", "/")]));
        var controller = new ProxyConfigLintController(new ProxyConfigLintAdministrationService(service));
        var actionResult = controller.Submitted(null);
        var badRequest = (BadRequestObjectResult)AssertEx.NotNull(actionResult.Result);
        var result = (ConfigLintResponse)AssertEx.NotNull(badRequest.Value);
        AssertEx.False(result.Succeeded);
        AssertFinding(result, "missing_request", "error");
        AssertEx.True(result.Findings[0].Message.Contains("request body is required", StringComparison.Ordinal));
        AssertEx.False(result.Findings is ConfigLintFindingResponse[], "Config lint API findings should not expose a mutable array.");
        AssertEx.False(result.ValidationErrors is ProxyConfigurationFileErrorResponse[], "Config lint API validation errors should not expose a mutable array.");
        var responseFindings = new List<ConfigLintFindingResponse>
        {
            result.Findings[0]
        };
        var responseValidationErrors = new List<ProxyConfigurationFileErrorResponse>
        {
            new("submitted.json", "parse failed")
        };
        var directResponse = new ConfigLintResponse(succeeded: false, lintedAtUtc: result.LintedAtUtc, summary: result.Summary, findings: responseFindings, validationErrors: responseValidationErrors);
        responseFindings[0] = responseFindings[0] with
        {
            Code = "replacement"
        };
        responseValidationErrors[0] = responseValidationErrors[0] with
        {
            Path = "replacement.json"
        };
        responseFindings.Clear();
        responseValidationErrors.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ConfigLintResponse(succeeded: false, lintedAtUtc: result.LintedAtUtc, summary: null!, findings: [], validationErrors: []));
        AssertEx.Throws<ArgumentNullException>(() => new ConfigLintResponse(succeeded: false, lintedAtUtc: result.LintedAtUtc, summary: result.Summary, findings: null!, validationErrors: []));
        AssertEx.Throws<ArgumentNullException>(() => new ConfigLintResponse(succeeded: false, lintedAtUtc: result.LintedAtUtc, summary: result.Summary, findings: [], validationErrors: null!));
        AssertEx.Equal("missing_request", directResponse.Findings[0].Code);
        AssertEx.Equal("submitted.json", directResponse.ValidationErrors[0].Path);
        AssertEx.False(directResponse.Findings is ConfigLintFindingResponse[], "Direct config lint API findings should not expose a mutable array.");
        AssertEx.False(directResponse.ValidationErrors is ProxyConfigurationFileErrorResponse[], "Direct config lint API validation errors should not expose a mutable array.");
    }

    public static void ConfigLintControllerRejectsIncompleteSubmittedRequestFields()
    {
        var service = CreateLintService(BaseOptions([ProxyRoute("active", "active.test", "/")]));
        var controller = new ProxyConfigLintController(new ProxyConfigLintAdministrationService(service));
        var missingFormat = controller.Submitted(new ProxyConfigLintSubmissionRequest(null, "submitted"));
        var missingText = controller.Submitted(new ProxyConfigLintSubmissionRequest("json", null));
        var formatResponse = (BadRequestObjectResult)AssertEx.NotNull(missingFormat.Result);
        var formatResult = (ConfigLintResponse)AssertEx.NotNull(formatResponse.Value);
        AssertFinding(formatResult, "invalid_format", "error");
        AssertEx.False(formatResult.Findings is ConfigLintFindingResponse[], "Config lint API findings should not expose a mutable array.");
        AssertEx.False(formatResult.ValidationErrors is ProxyConfigurationFileErrorResponse[], "Config lint API validation errors should not expose a mutable array.");
        var textResponse = (BadRequestObjectResult)AssertEx.NotNull(missingText.Result);
        var textResult = (ConfigLintResponse)AssertEx.NotNull(textResponse.Value);
        AssertFinding(textResult, "empty_config", "error");
        AssertEx.True(textResult.Findings[0].Message.Contains("config text is required", StringComparison.Ordinal));
        AssertEx.False(textResult.Findings is ConfigLintFindingResponse[], "Config lint API findings should not expose a mutable array.");
        AssertEx.False(textResult.ValidationErrors is ProxyConfigurationFileErrorResponse[], "Config lint API validation errors should not expose a mutable array.");
    }

    public static void RouteDiagnosticsControllerRejectsMissingMatchRequestBody()
    {
        var service = CreateRouteService(BaseOptions([ProxyRoute("active", "active.test", "/")]), out _, out _);
        var controller = new ProxyRouteDiagnosticsController(new ProxyRouteDiagnosticsAdministrationService(service));
        var actionResult = controller.Match(null);
        var badRequest = (BadRequestObjectResult)AssertEx.NotNull(actionResult.Result);
        var result = (RouteMatchDryRunResponse)AssertEx.NotNull(badRequest.Value);
        AssertEx.False(result.Succeeded);
        AssertEx.True(result.Findings.Any(static finding => finding.Code == "missing_request" && finding.Severity == "error"));
        AssertEx.False(result.Findings is RouteMatchDryRunFindingResponse[], "Route diagnostics API findings should not expose a mutable array.");
        var findings = new List<RouteMatchDryRunFindingResponse>
        {
            new("error", "missing_request", "Request body is required.")
        };
        var policy = new RouteMatchDryRunPolicyResponse(false, false, "no_route");
        var directResponse = new RouteMatchDryRunResponse(succeeded: false, evaluatedAtUtc: DateTimeOffset.UnixEpoch, failureReason: "missing_request", noMatchReason: null, listener: null, route: null, configuredAction: null, effectiveAction: null, wouldProxy: false, generatedStatusCode: null, originalTarget: null, rewrittenTarget: null, upstream: null, cache: policy, retry: policy, circuitBreaker: policy, findings: findings);
        findings[0] = new RouteMatchDryRunFindingResponse("warning", "replacement", "Replacement.");
        findings.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunResponse(succeeded: false, evaluatedAtUtc: DateTimeOffset.UnixEpoch, failureReason: "missing_request", noMatchReason: null, listener: null, route: null, configuredAction: null, effectiveAction: null, wouldProxy: false, generatedStatusCode: null, originalTarget: null, rewrittenTarget: null, upstream: null, cache: null!, retry: policy, circuitBreaker: policy, findings: []));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunResponse(succeeded: false, evaluatedAtUtc: DateTimeOffset.UnixEpoch, failureReason: "missing_request", noMatchReason: null, listener: null, route: null, configuredAction: null, effectiveAction: null, wouldProxy: false, generatedStatusCode: null, originalTarget: null, rewrittenTarget: null, upstream: null, cache: policy, retry: policy, circuitBreaker: policy, findings: null!));
        AssertEx.Equal("missing_request", directResponse.Findings[0].Code);
        AssertEx.Equal("no_route", directResponse.Cache.Reason);
        AssertEx.False(directResponse.Findings is RouteMatchDryRunFindingResponse[], "Direct route diagnostics API findings should not expose a mutable array.");
    }

    public static void RouteDiagnosticsRequestReaderAcceptsNormalizedInput()
    {
        var evaluatedAt = new DateTimeOffset(2026, 6, 13, 10, 0, 0, TimeSpan.Zero);
        var headers = new Dictionary<string, string?>
        {
            ["Authorization"] = "secret",
            ["X-Test"] = "value"
        };
        var request = new RouteMatchDryRunRequest("HTTPS", " diag.test ", 8443, "post", "/api", "id=1", headers, "127.0.0.1", "tls", "HTTP3");
        headers.Clear();
        var decision = ProxyRouteDiagnosticsRequestReader.Read(request, evaluatedAt, new Mk8.Drava.Application.INF.Proxy.RuntimeGuards.ProxyClientAddressSyntaxPolicy());
        AssertEx.True(decision is ProxyRouteDiagnosticsRequestDecision.AcceptedDecision);
        var accepted = (ProxyRouteDiagnosticsRequestDecision.AcceptedDecision)decision;
        AssertEx.Equal("https", accepted.Input.Scheme);
        AssertEx.Equal("http3", accepted.Input.Protocol!);
        AssertEx.Equal("/api?id=1", accepted.Input.Target);
        AssertEx.Equal("diag.test", accepted.Input.RequestHead.Host);
        AssertEx.True(accepted.Input.Findings.Any(static finding => finding.Code == "sensitive_header_redacted"));
        AssertEx.False(accepted.Input.Findings is List<RouteMatchDryRunFinding>, "Route diagnostics request findings should not expose a mutable list.");
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestDecision.AcceptedDecision(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteDiagnosticsRequestDecision.Accepted(null!));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestDecision.RejectedDecision(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyRouteDiagnosticsRequestDecision.Rejected(null!));
        AssertEx.Equal("HTTPS", request.Scheme);
        AssertEx.Equal(" diag.test ", request.Host);
        AssertEx.Equal("post", request.Method);
        AssertEx.Equal("/api", request.Path);
        AssertEx.Equal("id=1", request.Query);
        AssertEx.Equal("127.0.0.1", request.ClientIp);
        AssertEx.Equal("tls", request.ListenerName);
        AssertEx.Equal("HTTP3", request.Protocol);
        AssertEx.False(request.Headers is Dictionary<string, string?>, "Route diagnostics dry-run request headers should not expose a mutable dictionary.");
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunRequest(null!, "diag.test", null, "GET", "/", "", NoHeaders(), null, null));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunRequest("http", null!, null, "GET", "/", "", NoHeaders(), null, null));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunRequest("http", "diag.test", null, null!, "/", "", NoHeaders(), null, null));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunRequest("http", "diag.test", null, "GET", null!, "", NoHeaders(), null, null));
        AssertEx.Throws<ArgumentNullException>(() => new RouteMatchDryRunRequest("http", "diag.test", null, "GET", "/", "", null!, null, null));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestInput(null!, null, null, null, "/", "/", accepted.Input.RequestHead, false, []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestInput("http", null, null, null, null!, "/", accepted.Input.RequestHead, false, []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestInput("http", null, null, null, "/", null!, accepted.Input.RequestHead, false, []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestInput("http", null, null, null, "/", "/", null!, false, []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestInput("http", null, null, null, "/", "/", accepted.Input.RequestHead, false, null!));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestInput("http", null, null, null, "/", "/", accepted.Input.RequestHead, false, [null!]));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestHead(null!, "/", "/", "HTTP/1.1", "diag.test", ProxyRouteDiagnosticsRequestFraming.None, []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestHead("GET", null!, "/", "HTTP/1.1", "diag.test", ProxyRouteDiagnosticsRequestFraming.None, []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestHead("GET", "/", null!, "HTTP/1.1", "diag.test", ProxyRouteDiagnosticsRequestFraming.None, []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestHead("GET", "/", "/", null!, "diag.test", ProxyRouteDiagnosticsRequestFraming.None, []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestHead("GET", "/", "/", "HTTP/1.1", null!, ProxyRouteDiagnosticsRequestFraming.None, []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestHead("GET", "/", "/", "HTTP/1.1", "diag.test", null!, []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestHead("GET", "/", "/", "HTTP/1.1", "diag.test", ProxyRouteDiagnosticsRequestFraming.None, null!));
        AssertEx.Throws<ArgumentException>(() => new ProxyRouteDiagnosticsRequestFraming(ProxyRouteDiagnosticsBodyKind.None, 1));
        AssertEx.Throws<ArgumentException>(() => new ProxyRouteDiagnosticsRequestFraming(ProxyRouteDiagnosticsBodyKind.Chunked, 1));
        AssertEx.Throws<ArgumentException>(() => new ProxyRouteDiagnosticsRequestFraming(ProxyRouteDiagnosticsBodyKind.ContentLength, null));
        AssertEx.Throws<ArgumentException>(() => new ProxyRouteDiagnosticsRequestFraming(ProxyRouteDiagnosticsBodyKind.ContentLength, 0));
        AssertEx.Throws<ArgumentException>(() => new ProxyRouteDiagnosticsRequestFraming(ProxyRouteDiagnosticsBodyKind.CloseDelimited, null));
        AssertEx.Throws<ArgumentException>(() => new ProxyRouteDiagnosticsRequestFraming((ProxyRouteDiagnosticsBodyKind)99, null));
    }

    public static void RouteDiagnosticsRequestReaderRejectsInvalidScheme()
    {
        var evaluatedAt = new DateTimeOffset(2026, 6, 13, 10, 0, 0, TimeSpan.Zero);
        var request = new RouteMatchDryRunRequest("ftp", "diag.test", null, "GET", "/", "", NoHeaders(), null, null);
        var decision = ProxyRouteDiagnosticsRequestReader.Read(request, evaluatedAt, new Mk8.Drava.Application.INF.Proxy.RuntimeGuards.ProxyClientAddressSyntaxPolicy());
        AssertEx.True(decision is ProxyRouteDiagnosticsRequestDecision.RejectedDecision);
        var rejected = (ProxyRouteDiagnosticsRequestDecision.RejectedDecision)decision;
        AssertEx.Equal("invalid_scheme", rejected.Failure.FailureReason);
    }

    public static void RouteDiagnosticsControllerMapsMissingHeadersToEmptyInput()
    {
        var service = CreateRouteService(BaseOptions([ProxyRoute("active", "active.test", "/")]), out _, out _);
        var controller = new ProxyRouteDiagnosticsController(new ProxyRouteDiagnosticsAdministrationService(service));
        var actionResult = controller.Match(new ProxyRouteMatchDryRunRequest("http", "active.test", 8080, "GET", "/", "", null, null, null));
        var ok = (OkObjectResult)AssertEx.NotNull(actionResult.Result);
        var result = (RouteMatchDryRunResponse)AssertEx.NotNull(ok.Value);
        AssertEx.True(result.Succeeded);
        AssertEx.Equal("active", AssertEx.NotNull(result.Route).Name);
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = "secret-token"
        };
        var apiRequest = new ProxyRouteMatchDryRunRequest("http", "active.test", 8080, "GET", "/", "", headers, null, null);
        headers["Authorization"] = "replacement";
        headers.Clear();
        var bllRequest = apiRequest.ToRouteMatchDryRunRequest();
        var missingHeadersRequest = new ProxyRouteMatchDryRunRequest("http", "active.test", 8080, "GET", "/", "", null, null, null).ToRouteMatchDryRunRequest();
        AssertEx.Equal("secret-token", AssertEx.NotNull(apiRequest.Headers)["Authorization"]);
        AssertEx.Equal("secret-token", bllRequest.Headers["Authorization"]);
        AssertEx.Equal(0, missingHeadersRequest.Headers.Count);
        AssertEx.False(apiRequest.Headers is Dictionary<string, string?>, "Route diagnostics API dry-run request headers should not expose a mutable dictionary.");
    }

    public static async Task DiagnosticEndpointsRequireAdminAuth()
    {
        var store = CreateStore(BaseOptions([ProxyRoute("active", "active.test", "/")]));
        var audit = new AdminAuditStore(SilentLogPersistenceStore.Instance);
        var metrics = new ProxyMetrics();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/admin/proxy/routes/match";
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        var middleware = ProxyAdminAuthenticationTestFactory.CreateMiddleware(_ => Task.CompletedTask, store, audit, metrics);
        await middleware.InvokeAsync(context).ConfigureAwait(false);
        AssertEx.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    public static void MetricsIncludeLintAndRouteDryRunCounters()
    {
        var options = BaseOptions([ProxyRoute("catch-all", "*", "/"), ProxyRoute("api", "diag.test", "/api")]);
        var routeService = CreateRouteService(options, out var store, out var metrics);
        var lintService = CreateLintService(options, store, metrics);
        _ = routeService.Explain(new RouteMatchDryRunRequest("http", "diag.test", 8080, "GET", "/api", "", NoHeaders(), null, null));
        _ = lintService.LintActive();
        var snapshot = metrics.Snapshot();
        AssertEx.Equal(1L, snapshot.RouteDiagnostics.DryRuns);
        AssertEx.Equal(1L, snapshot.ConfigLint.Runs);
        AssertEx.True(snapshot.ConfigLint.Findings.Any(static finding => finding.Code == "route_shadowed"));
    }

    private static RouteMatchDiagnosticsService CreateRouteService(ProxyOptions options, out ProxyConfigurationStore store, out ProxyMetrics metrics)
    {
        store = CreateStore(options);
        metrics = new ProxyMetrics();
        return new RouteMatchDiagnosticsService(new ProxyRouteDiagnosticsConfigurationSource(store), new ProxyRouteDiagnosticsMatcher(), new ProxyRouteDiagnosticsActionPolicyAdapter(), new ProxyRouteDiagnosticsPathRewritePolicyAdapter(), metrics, new Mk8.Drava.Application.INF.Proxy.RuntimeGuards.ProxyClientAddressSyntaxPolicy(), TimeProvider.System);
    }

    private static ProxyCacheStatus CacheStatus(ResponseCacheStore cache, ProxyConfigurationSnapshot? configuration)
    {
        return ProxyCacheStatusReader.Project(configuration is null ? [] : ProxyCacheStatusRouteSourceMapper.ToRouteSources(configuration.Routes), cache.ReadStatusSnapshot());
    }

    private static RouteMatchDiagnosticsService CreateBllRouteService(IProxyRouteDiagnosticsConfigurationSnapshot? snapshot, FixedRouteDiagnosticsMatcher? matcher = null, FixedRouteDiagnosticsActionPolicy? actionPolicy = null, FixedRouteDiagnosticsPathRewritePolicy? pathRewritePolicy = null, FixedRouteDiagnosticsMetricsSink? metricsSink = null)
    {
        return new RouteMatchDiagnosticsService(new FixedRouteDiagnosticsConfigurationSource(snapshot), matcher ?? new FixedRouteDiagnosticsMatcher(), actionPolicy ?? new FixedRouteDiagnosticsActionPolicy(ProxyRouteDiagnosticsActionDecision.Proxy), pathRewritePolicy ?? new FixedRouteDiagnosticsPathRewritePolicy(), metricsSink ?? new FixedRouteDiagnosticsMetricsSink(), new Mk8.Drava.Application.INF.Proxy.RuntimeGuards.ProxyClientAddressSyntaxPolicy(), TimeProvider.System);
    }

    private static ConfigLintService CreateLintService(ProxyOptions options)
    {
        return CreateLintService(options, out _);
    }

    private static ConfigLintService CreateLintService(ProxyOptions options, out ProxyConfigurationStore store)
    {
        store = CreateStore(options);
        return CreateLintService(options, store, new ProxyMetrics());
    }

    private static ConfigLintService CreateLintService(ProxyOptions _, ProxyConfigurationStore store, ProxyMetrics metrics)
    {
        return new ConfigLintService(new ProxyConfigLintActiveConfigurationSource(store, TestHttp3PlatformSupport.SupportedSource), new ProxyConfigLintSubmittedConfigurationSource(new SiteConfigurationParser(), TestHttp3PlatformSupport.SupportedSource, new ProxyEndpointAddressPolicy(), new ProxyUrlSyntaxPolicy()), new ProxyConfigLintRuntimeStateSource(new ProxyRuntimeState(TimeProvider.System)), metrics, new ProxyConfigLintSourceNameFormatter(), new ProxyAdminUrlPolicy(), TimeProvider.System);
    }

    private static ProxyConfigurationStore CreateStore(ProxyOptions options)
    {
        var store = new ProxyConfigurationStore();
        store.Replace(Snapshot(options));
        return store;
    }

    private static ProxyConfigurationSnapshot Snapshot(ProxyOptions options)
    {
        var operationalOptions = new ProxyOperationalOptions
        {
            Admin = new ProxyAdminOptions
            {
                RequireAuthentication = true,
                Token = AdminToken
            }
        };
        return ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(options, operationalOptions, ProxyAdminSecurityTokenPolicy.Resolve(operationalOptions.Admin, static _ => null), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), 1, DateTimeOffset.UnixEpoch, "test", ["site.json"], new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("test", "test/config", "test/config/sites", "test/logs", "test/certs", "test/state", "test/config/proxy.json"), [], [], []));
    }

    private static ProxyConfigurationSnapshot RuntimeSnapshot(IReadOnlyList<RuntimeRoute> routes, IReadOnlyList<RuntimeListener>? listeners = null)
    {
        return new ProxyConfigurationSnapshot(1, DateTimeOffset.UnixEpoch, "test", ["site.json"], new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("test", "test/config", "test/config/sites", "test/logs", "test/certs", "test/state", "test/config/proxy.json"), [], [], []), new RuntimeAdminSecurityOptions([], true, true, AdminToken, "MDRAVA_ADMIN_TOKEN", "configured", 100), new RuntimeAcmeOptions(false, true, "", [], false, "acme", 30, 720, 60, []), new RuntimeTimeouts(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)), new RuntimeConnectionLimits(100, 16, 1024), new RuntimeObservabilityOptions(true, 100, new RuntimeLogPersistenceOptions(true, true, 1_048_576, 8)), new RuntimeLimits(4096, 128, 240, 30, 32768, 128, 8192, 104857600, 8192, TimeSpan.FromSeconds(15)), new RuntimeForwardedHeadersOptions(true, []), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), listeners ?? [], routes);
    }

    private static IReadOnlyDictionary<string, string?> NoHeaders()
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }

    private static ProxyOptions BaseOptions(IReadOnlyList<ProxyRouteOptions> routes)
    {
        return new ProxyOptions
        {
            Listeners = [new ListenerOptions
            {
                Name = "web",
                Address = "127.0.0.1",
                Port = 8080,
                Transport = "http"
            }

            ],
            Routes = new System.Collections.ObjectModel.Collection<Mk8.Drava.Application.BLL.Configuration.ProxyRouteOptions>(routes.ToList())
        };
    }

    private static ProxyRouteOptions ProxyRoute(string name, string host, string pathPrefix, int upstreamPort = 5000, string scheme = "http", ProxyCachePolicyOptions? cache = null, ProxyRetryPolicyOptions? retry = null, UpstreamTlsOptions? upstreamTls = null, ProxyCircuitBreakerOptions? circuitBreaker = null, ProxyPathRewriteOptions? pathRewrite = null, string upstreamProtocol = RuntimeUpstreamProtocol.Http1)
    {
        return new ProxyRouteOptions
        {
            Name = name,
            SiteName = "diag",
            Host = host,
            PathPrefix = pathPrefix,
            Action = "proxy",
            Upstreams = [new UpstreamOptions
            {
                Name = "local",
                Scheme = scheme,
                Protocol = upstreamProtocol,
                Address = "127.0.0.1",
                Port = upstreamPort,
                UpstreamTls = upstreamTls ?? new UpstreamTlsOptions(),
                CircuitBreaker = circuitBreaker ?? new ProxyCircuitBreakerOptions()
            }

            ],
            Cache = cache ?? new ProxyCachePolicyOptions(),
            Retry = retry ?? new ProxyRetryPolicyOptions(),
            PathRewrite = pathRewrite ?? new ProxyPathRewriteOptions()
        };
    }

    private static ProxyCachePolicyOptions CachePolicy()
    {
        return new ProxyCachePolicyOptions
        {
            Enabled = true,
            MaxEntryBytes = 4096,
            MaxTotalBytes = 8192,
            DefaultTtlSeconds = 60,
            Methods = ["GET", "HEAD"],
            CacheableStatusCodes = [200]
        };
    }

    private static ProxyRetryPolicyOptions RetryPolicy()
    {
        return new ProxyRetryPolicyOptions
        {
            Enabled = true,
            MaxAttempts = 2,
            RetryOnConnectFailure = true,
            RetryMethods = ["GET", "HEAD"]
        };
    }

    private static Http1RequestHead Request(string method, string target, string path, string host)
    {
        return new Http1RequestHead(method, target, path, "HTTP/1.1", host, Http1RequestFraming.None, []);
    }

    private static string SubmittedSiteJson(string name, string host)
    {
        return $$"""
        {
          "name": "{{name}}",
          "listeners": [
            {
              "name": "web",
              "address": "127.0.0.1",
              "port": 8080,
              "transport": "http"
            }
          ],
          "host": "{{host}}",
          "routes": [
            {
              "name": "app",
              "pathPrefix": "/",
              "action": "proxy",
              "upstreams": [
                {
                  "name": "local",
                  "address": "127.0.0.1",
                  "port": 5000
                }
              ]
            }
          ]
        }
        """;
    }

    private static ProxyConfigLintConfigurationSnapshot LintSnapshot(string sourceFile)
    {
        return new ProxyConfigLintConfigurationSnapshot([sourceFile], new ProxyConfigLintAdminSecurity([AdminBindPolicy.DefaultAdminUrl], true), new ProxyConfigLintMetricsOptions(false), true, [new ProxyConfigLintListener("web", "127.0.0.1", 8080, true, "Http", false, false, "not_configured", "disabled", false, null)], [LintRoute("catch-all", "*", "/"), LintRoute("api", "diag.test", "/api")]);
    }

    private static ProxyConfigLintRoute LintRoute(string name, string host, string pathPrefix)
    {
        return new ProxyConfigLintRoute(name, "diag", host, pathPrefix, "Proxy", false, false, "", false, [], false, ["GET", "HEAD"], false, [new ProxyConfigLintUpstream("local", "http", RuntimeUpstreamProtocol.Http1, true, false)], "");
    }

    private sealed class FixedConfigLintActiveConfigurationSource : IProxyConfigLintActiveConfigurationSource
    {
        private readonly ProxyConfigLintConfigurationSnapshot? _snapshot;
        public FixedConfigLintActiveConfigurationSource(ProxyConfigLintConfigurationSnapshot? snapshot)
        {
            _snapshot = snapshot;
        }

        public ProxyConfigLintActiveConfigurationReadResult Read()
        {
            return _snapshot is null ? ProxyConfigLintActiveConfigurationReadResult.MissingConfiguration : ProxyConfigLintActiveConfigurationReadResult.Available(_snapshot);
        }
    }

    private sealed class FixedConfigLintSubmittedConfigurationSource : IProxyConfigLintSubmittedConfigurationSource
    {
        private readonly ProxyConfigLintSubmittedConfigurationResult? _result;
        public FixedConfigLintSubmittedConfigurationSource(ProxyConfigLintSubmittedConfigurationResult? result)
        {
            _result = result;
        }

        public ProxyConfigurationNormalizeFormat? LastFormat { get; private set; }

        public ProxyConfigLintSubmittedConfigurationResult Read(string text, ProxyConfigurationNormalizeFormat format, DateTimeOffset loadedAtUtc)
        {
            _ = text;
            _ = loadedAtUtc;
            LastFormat = format;
            return _result ?? ProxyConfigLintSubmittedConfigurationResult.Empty();
        }
    }

    private sealed class FixedConfigLintRuntimeStateSource : IProxyConfigLintRuntimeStateSource
    {
        private readonly IReadOnlyList<ProxyConfigLintRuntimeListenerState> _listeners;
        public FixedConfigLintRuntimeStateSource(IReadOnlyList<ProxyConfigLintRuntimeListenerState> listeners)
        {
            _listeners = listeners;
        }

        public IReadOnlyList<ProxyConfigLintRuntimeListenerState> GetListenerStates()
        {
            return _listeners;
        }
    }

    private sealed class FixedConfigLintMetricsSink : IProxyConfigLintMetricsSink
    {
        public IReadOnlyList<ConfigLintFinding> LastFindings { get; private set; } = [];

        public void ConfigLintRun(IReadOnlyList<ConfigLintFinding> findings)
        {
            LastFindings = findings;
        }
    }

    private static FixedRouteDiagnosticsListener RouteDiagnosticsListener(string name, string transport, int port, RuntimeListenerProtocols protocols, bool http3EnabledForTraffic)
    {
        return new FixedRouteDiagnosticsListener(name, transport, "127.0.0.1", port, true, protocols.HasFlag(RuntimeListenerProtocols.Http1), protocols.HasFlag(RuntimeListenerProtocols.Http2), protocols.HasFlag(RuntimeListenerProtocols.Http3), http3EnabledForTraffic);
    }

    private static FixedRouteDiagnosticsRoute RouteDiagnosticsRoute(string name, string host, string pathPrefix, bool cacheEnabled = false, bool retryEnabled = false, bool circuitBreakerEnabled = false)
    {
        return new FixedRouteDiagnosticsRoute("diag", name, host, pathPrefix, "Proxy", false, new ProxyRouteDiagnosticsMaintenancePolicy(false, null, "text/plain", ""), new ProxyRouteDiagnosticsHttpsRedirectPolicy(false, 308, null), new ProxyRouteDiagnosticsCanonicalHostPolicy(false, "", 308), new ProxyRouteDiagnosticsRedirectPolicy(302, "", "", false), new ProxyRouteDiagnosticsStaticResponse(200, "text/plain", ""), new ProxyRouteDiagnosticsPathRewrite("", "", ""), 10 * 1024 * 1024, [new FixedRouteDiagnosticsUpstream("local", "http", RuntimeUpstreamProtocol.Http1, "127.0.0.1:5000", 1, circuitBreakerEnabled)], cacheEnabled, ["GET", "HEAD"], retryEnabled, ["GET", "HEAD"]);
    }

    private static ProxyListenerStatus ListenerStatus(string identity, string kind, ProxyListenerState state)
    {
        return new ProxyListenerStatus("main", identity, $"{identity}|bind", kind, "127.0.0.1", 18080, kind == "quic" ? "udp/quic" : "http", TlsEnabled: false, RuntimeListenerProtocols.Http1.ToConfigText(), new ProxyListenerHttp3Status(Configured: false, DefaultEnabled: false, EnablementLevel: "disabled", EnabledForTraffic: false, DisabledReason: "disabled", AltSvcConfigured: false, AltSvcMaxAgeSeconds: 0, UdpQuicListenerIdentityModeled: false, QuicIdentity: null), Http2MaxConcurrentStreams: 100, Http2MaxHeaderListBytes: 32768, Http2MaxFrameSize: 16384, state, ActiveConnections: 0, StartedAtUtc: null, StoppedAtUtc: null, LastError: state == ProxyListenerState.Failed ? "bind_failed" : null);
    }

    private sealed record FixedRouteDiagnosticsConfigurationSnapshot(IReadOnlyList<IProxyRouteDiagnosticsListener> Listeners, IReadOnlyList<IProxyRouteDiagnosticsRoute> Routes) : IProxyRouteDiagnosticsConfigurationSnapshot;
    private sealed class FixedRouteDiagnosticsConfigurationSource : IProxyRouteDiagnosticsConfigurationSource
    {
        private readonly IProxyRouteDiagnosticsConfigurationSnapshot? _snapshot;
        public FixedRouteDiagnosticsConfigurationSource(IProxyRouteDiagnosticsConfigurationSnapshot? snapshot)
        {
            _snapshot = snapshot;
        }

        public ProxyRouteDiagnosticsConfigurationReadResult Read()
        {
            return _snapshot is null ? ProxyRouteDiagnosticsConfigurationReadResult.MissingConfiguration : ProxyRouteDiagnosticsConfigurationReadResult.Available(_snapshot);
        }
    }

    private sealed class FixedRouteDiagnosticsMatcher : IProxyRouteDiagnosticsMatcher
    {
        public IProxyRouteDiagnosticsRoute? Match(IReadOnlyList<IProxyRouteDiagnosticsRoute> routes, ProxyRouteDiagnosticsRequestHead requestHead)
        {
            return routes.FirstOrDefault(route => HostMatches(route.Host, requestHead.Host) && requestHead.Path.StartsWith(route.PathPrefix, StringComparison.Ordinal));
        }

        private static bool HostMatches(string configuredHost, string requestHost)
        {
            return string.Equals(configuredHost, "*", StringComparison.Ordinal) || string.Equals(configuredHost, requestHost, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FixedRouteDiagnosticsActionPolicy : IProxyRouteDiagnosticsActionPolicy
    {
        private readonly ProxyRouteDiagnosticsActionDecision _decision;
        public FixedRouteDiagnosticsActionPolicy(ProxyRouteDiagnosticsActionDecision decision)
        {
            _decision = decision;
        }

        public IProxyRouteDiagnosticsListener? LastListener { get; private set; }
        public ProxyRouteDiagnosticsRequestHead? LastRequestHead { get; private set; }

        public ProxyRouteDiagnosticsActionDecision Evaluate(IProxyRouteDiagnosticsRoute route, ProxyRouteDiagnosticsRequestHead requestHead, IProxyRouteDiagnosticsListener listener, bool isUpgradeRequest)
        {
            _ = route;
            _ = isUpgradeRequest;
            LastRequestHead = requestHead;
            LastListener = listener;
            return _decision;
        }
    }

    private sealed class FixedRouteDiagnosticsPathRewritePolicy : IProxyRouteDiagnosticsPathRewritePolicy
    {
        public string Apply(IProxyRouteDiagnosticsRoute route, string target, string path)
        {
            _ = route;
            _ = path;
            return target;
        }
    }

    private sealed class FixedRouteDiagnosticsMetricsSink : IProxyRouteDiagnosticsMetricsSink
    {
        public string? LastFailureReason { get; private set; }

        public void RouteMatchDryRun(string? failureReason)
        {
            LastFailureReason = failureReason;
        }
    }

    private sealed record FixedRouteDiagnosticsListener(string Name, string Transport, string Address, int Port, bool Enabled, bool SupportsHttp1, bool SupportsHttp2, bool SupportsHttp3, bool Http3EnabledForTraffic) : IProxyRouteDiagnosticsListener;
    private sealed record FixedRouteDiagnosticsRoute(string SiteName, string Name, string Host, string PathPrefix, string Action, bool MaintenanceEnabled, ProxyRouteDiagnosticsMaintenancePolicy Maintenance, ProxyRouteDiagnosticsHttpsRedirectPolicy HttpsRedirect, ProxyRouteDiagnosticsCanonicalHostPolicy CanonicalHost, ProxyRouteDiagnosticsRedirectPolicy Redirect, ProxyRouteDiagnosticsStaticResponse StaticResponse, ProxyRouteDiagnosticsPathRewrite PathRewrite, long MaxRequestBodyBytes, IReadOnlyList<IProxyRouteDiagnosticsUpstream> Upstreams, bool CacheEnabled, IReadOnlyList<string> CacheMethods, bool RetryEnabled, IReadOnlyList<string> RetryMethods) : IProxyRouteDiagnosticsRoute;
    private sealed record FixedRouteDiagnosticsUpstream(string Name, string Scheme, string Protocol, string Endpoint, int Weight, bool CircuitBreakerEnabled) : IProxyRouteDiagnosticsUpstream;
    private static RouteMatchDryRunResult.MatchedRouteResult Matched(RouteMatchDryRunResult result)
    {
        AssertEx.True(result is RouteMatchDryRunResult.MatchedRouteResult);
        return (RouteMatchDryRunResult.MatchedRouteResult)result;
    }

    private static RouteMatchDryRunResult.NoMatchingRouteResult NoMatchingRoute(RouteMatchDryRunResult result)
    {
        AssertEx.True(result is RouteMatchDryRunResult.NoMatchingRouteResult);
        return (RouteMatchDryRunResult.NoMatchingRouteResult)result;
    }

    private static RouteMatchDryRunResult.NoMatchingListenerResult NoMatchingListener(RouteMatchDryRunResult result)
    {
        AssertEx.True(result is RouteMatchDryRunResult.NoMatchingListenerResult);
        return (RouteMatchDryRunResult.NoMatchingListenerResult)result;
    }

    private static void AssertFinding(ConfigLintResult result, string code, string severity)
    {
        AssertEx.True(result.Findings.Any(finding => finding.Code == code && finding.Severity == severity), string.Join("; ", result.Findings.Select(static finding => $"{finding.Severity}:{finding.Code}:{finding.Message}")));
    }

    private static void AssertFinding(ConfigLintResponse result, string code, string severity)
    {
        AssertEx.True(result.Findings.Any(finding => finding.Code == code && finding.Severity == severity), string.Join("; ", result.Findings.Select(static finding => $"{finding.Severity}:{finding.Code}:{finding.Message}")));
    }

    private static void AssertNoFinding(ConfigLintResult result, string code)
    {
        AssertEx.False(result.Findings.Any(finding => finding.Code == code), string.Join("; ", result.Findings.Select(static finding => $"{finding.Severity}:{finding.Code}:{finding.Message}")));
    }

    private static void AssertAccepted(ConfigLintResult result, string? message = null)
    {
        AssertEx.True(result is ConfigLintResult.AcceptedResult, message ?? string.Join("; ", result.Findings.Select(static finding => finding.Message)));
    }

    private static void AssertRejected(ConfigLintResult result, string? message = null)
    {
        AssertEx.True(result is ConfigLintResult.RejectedResult, message ?? string.Join("; ", result.Findings.Select(static finding => finding.Message)));
    }
}
