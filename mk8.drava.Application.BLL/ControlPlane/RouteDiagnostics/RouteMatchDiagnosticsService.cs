using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;

namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed partial class RouteMatchDiagnosticsService : IProxyRouteDiagnosticsOperations
{
    private static string? MetricReason(RouteMatchDryRunResult result)
    {
        return result switch
        {
            RouteMatchDryRunResult.FailedResult failed => failed.FailureReason,
            RouteMatchDryRunResult.NoMatchingListenerResult noListener => noListener.NoMatchReason,
            RouteMatchDryRunResult.NoMatchingRouteResult noRoute => noRoute.NoMatchReason,
            RouteMatchDryRunResult.MatchedRouteResult matched => matched.NoMatchReason,
            _ => throw new InvalidOperationException($"Unknown route dry-run result '{result.GetType().Name}'.")};
    }

    private static RouteMatchDryRunUpstream? SelectDiagnosticUpstream(IProxyRouteDiagnosticsRoute route)
    {
        var upstream = route.Upstreams.FirstOrDefault(static candidate => candidate.Weight > 0);
        return upstream is null ? null : new RouteMatchDryRunUpstream(upstream.Name, upstream.Scheme, upstream.Protocol, upstream.Endpoint, upstream.Weight, "first_configured_candidate_no_state_mutation");
    }

    private static string EffectiveAction(IProxyRouteDiagnosticsRoute route, ProxyRouteDiagnosticsActionDecision actionDecision)
    {
        if (actionDecision.ShouldProxy)
        {
            return "proxy";
        }

        if (route.MaintenanceEnabled)
        {
            return "maintenance";
        }

        if (string.Equals(route.Action, "Proxy", StringComparison.OrdinalIgnoreCase))
        {
            return "policyRedirect";
        }

        return RouteActionText(route.Action);
    }

    private static string RouteActionText(string action)
    {
        if (string.Equals(action, "Redirect", StringComparison.OrdinalIgnoreCase) || string.Equals(action, "redirect", StringComparison.OrdinalIgnoreCase))
        {
            return "redirect";
        }

        if (string.Equals(action, "StaticResponse", StringComparison.OrdinalIgnoreCase) || string.Equals(action, "staticResponse", StringComparison.OrdinalIgnoreCase))
        {
            return "staticResponse";
        }

        return "proxy";
    }

    private static RouteMatchDryRunListener ToListener(IProxyRouteDiagnosticsListener listener)
    {
        return new RouteMatchDryRunListener(listener.Name, string.Equals(listener.Transport, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http", listener.Address, listener.Port, ProtocolsText(listener));
    }

    private static string ProtocolsText(IProxyRouteDiagnosticsListener listener)
    {
        return (listener.SupportsHttp1, listener.SupportsHttp2, listener.SupportsHttp3) switch
        {
            (true, true, true) => "Http1AndHttp2AndHttp3",
            (true, true, false) => "Http1AndHttp2",
            (true, false, true) => "Http1AndHttp3",
            (false, true, true) => "Http2AndHttp3",
            (true, false, false) => "Http1",
            (false, true, false) => "Http2",
            (false, false, true) => "Http3",
            _ => "None"
        };
    }

    private readonly IProxyRouteDiagnosticsConfigurationSource _configurationSource;
    private readonly IProxyRouteDiagnosticsMatcher _routeMatcher;
    private readonly IProxyRouteDiagnosticsActionPolicy _routeActionPolicy;
    private readonly IProxyRouteDiagnosticsPathRewritePolicy _pathRewritePolicy;
    private readonly IProxyRouteDiagnosticsMetricsSink _metricsSink;
    private readonly IProxyClientAddressSyntaxPolicy _clientAddressSyntaxPolicy;
    private readonly TimeProvider _timeProvider;
    public RouteMatchDiagnosticsService(IProxyRouteDiagnosticsConfigurationSource configurationSource, IProxyRouteDiagnosticsMatcher routeMatcher, IProxyRouteDiagnosticsActionPolicy routeActionPolicy, IProxyRouteDiagnosticsPathRewritePolicy pathRewritePolicy, IProxyRouteDiagnosticsMetricsSink metricsSink, IProxyClientAddressSyntaxPolicy clientAddressSyntaxPolicy, TimeProvider timeProvider)
    {
        _configurationSource = configurationSource;
        _routeMatcher = routeMatcher;
        _routeActionPolicy = routeActionPolicy;
        _pathRewritePolicy = pathRewritePolicy;
        _metricsSink = metricsSink;
        _clientAddressSyntaxPolicy = clientAddressSyntaxPolicy;
        _timeProvider = timeProvider;
    }

    public RouteMatchDryRunResult Explain(RouteMatchDryRunRequest? request)
    {
        var evaluatedAtUtc = _timeProvider.GetUtcNow();
        var configuration = _configurationSource.Read();
        if (configuration is not ProxyRouteDiagnosticsConfigurationReadResult.AvailableResult available)
        {
            return Complete(Failure(evaluatedAtUtc, "no_active_config", "No active proxy configuration is loaded."));
        }

        var snapshot = available.Snapshot;
        var requestDecision = ProxyRouteDiagnosticsRequestReader.Read(request, evaluatedAtUtc, _clientAddressSyntaxPolicy);
        if (requestDecision is ProxyRouteDiagnosticsRequestDecision.RejectedDecision rejectedRequest)
        {
            return Complete(rejectedRequest.Failure);
        }

        var requestInput = ((ProxyRouteDiagnosticsRequestDecision.AcceptedDecision)requestDecision).Input;
        var listener = ProxyRouteDiagnosticsListenerSelector.Select(snapshot.Listeners, requestInput.ListenerName, requestInput.Scheme, requestInput.Port, requestInput.Protocol);
        if (listener is null)
        {
            return Complete(RouteMatchDryRunResult.NoMatchingListener(evaluatedAtUtc, requestInput.Target));
        }

        var route = _routeMatcher.Match(snapshot.Routes, requestInput.RequestHead);
        if (route is null)
        {
            return Complete(RouteMatchDryRunResult.NoMatchingRoute(evaluatedAtUtc, ToListener(listener), requestInput.Target));
        }

        var actionDecision = _routeActionPolicy.Evaluate(route, requestInput.RequestHead, listener, requestInput.IsUpgradeRequest);
        var generatedStatusCode = actionDecision.GeneratedStatusCode;
        var effectiveAction = EffectiveAction(route, actionDecision);
        var rewrittenTarget = _pathRewritePolicy.Apply(route, requestInput.Target, requestInput.Path);
        var wouldProxy = actionDecision.ShouldProxy;
        string? noMatchReason = null;
        if (wouldProxy && requestInput.RequestHead.Framing.Kind == ProxyRouteDiagnosticsBodyKind.ContentLength && requestInput.RequestHead.Framing.ContentLength.GetValueOrDefault() > route.MaxRequestBodyBytes)
        {
            wouldProxy = false;
            noMatchReason = "request_body_too_large";
            requestInput.AddFinding(new RouteMatchDryRunFinding("warning", "request_body_too_large", "The request body would exceed the matched route body limit."));
        }

        var upstream = wouldProxy ? SelectDiagnosticUpstream(route) : null;
        if (wouldProxy && upstream is null)
        {
            noMatchReason = "no_configured_upstream";
            requestInput.AddFinding(new RouteMatchDryRunFinding("warning", "no_configured_upstream", "The matched proxy route has no configured upstream candidate."));
        }

        return Complete(RouteMatchDryRunResult.MatchedRoute(evaluatedAtUtc, noMatchReason, ToListener(listener), new RouteMatchDryRunRoute(route.SiteName, route.Name, route.Host, route.PathPrefix), RouteActionText(route.Action), effectiveAction, wouldProxy && upstream is not null, generatedStatusCode, requestInput.Target, rewrittenTarget, upstream, ProxyRouteDiagnosticsPolicyExplainer.ExplainCache(route, requestInput.RequestHead, actionDecision.ShouldProxy), ProxyRouteDiagnosticsPolicyExplainer.ExplainRetry(route, requestInput.RequestHead, actionDecision.ShouldProxy), ProxyRouteDiagnosticsPolicyExplainer.ExplainCircuitBreaker(route, actionDecision.ShouldProxy), requestInput.Findings));
    }

    private RouteMatchDryRunResult Complete(RouteMatchDryRunResult result)
    {
        _metricsSink.RouteMatchDryRun(MetricReason(result));
        return result;
    }

    private static RouteMatchDryRunResult Failure(DateTimeOffset evaluatedAtUtc, string reason, string message)
    {
        return RouteMatchDryRunResult.Failed(evaluatedAtUtc, reason, message);
    }
}
