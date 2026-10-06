using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public static class ProxyRouteDiagnosticsPolicyExplainer
{
    public static RouteMatchDryRunPolicy Disabled(string reason)
    {
        return RouteMatchDryRunPolicy.Disabled(reason);
    }

    public static RouteMatchDryRunPolicy ExplainCache(IProxyRouteDiagnosticsRoute route, ProxyRouteDiagnosticsRequestHead requestHead, bool wouldProxy)
    {
        ArgumentNullException.ThrowIfNull(requestHead);
        ArgumentNullException.ThrowIfNull(route);
        if (!route.CacheEnabled)
        {
            return Disabled("disabled");
        }

        if (!wouldProxy)
        {
            return RouteMatchDryRunPolicy.EnabledButBlocked("not_proxy_action");
        }

        if (!route.CacheMethods.Any(method => string.Equals(method, requestHead.Method, StringComparison.OrdinalIgnoreCase)))
        {
            return RouteMatchDryRunPolicy.EnabledButBlocked("method_not_cacheable");
        }

        if (requestHead.Framing.Kind != ProxyRouteDiagnosticsBodyKind.None)
        {
            return RouteMatchDryRunPolicy.EnabledButBlocked("request_body");
        }

        if (ContainsHeader(requestHead.Headers, "Authorization"))
        {
            return RouteMatchDryRunPolicy.EnabledButBlocked("authorization");
        }

        return RouteMatchDryRunPolicy.EnabledAndApplies("eligible_before_origin_response");
    }

    public static RouteMatchDryRunPolicy ExplainRetry(IProxyRouteDiagnosticsRoute route, ProxyRouteDiagnosticsRequestHead requestHead, bool wouldProxy)
    {
        ArgumentNullException.ThrowIfNull(requestHead);
        ArgumentNullException.ThrowIfNull(route);
        if (!route.RetryEnabled)
        {
            return Disabled("disabled");
        }

        if (!wouldProxy)
        {
            return RouteMatchDryRunPolicy.EnabledButBlocked("not_proxy_action");
        }

        if (!route.RetryMethods.Any(method => string.Equals(method, requestHead.Method, StringComparison.OrdinalIgnoreCase)))
        {
            return RouteMatchDryRunPolicy.EnabledButBlocked("method_not_retryable");
        }

        if (requestHead.Framing.Kind != ProxyRouteDiagnosticsBodyKind.None)
        {
            return RouteMatchDryRunPolicy.EnabledButBlocked("request_body_not_replayable");
        }

        return RouteMatchDryRunPolicy.EnabledAndApplies("eligible_for_configured_transport_or_status_failures");
    }

    public static RouteMatchDryRunPolicy ExplainCircuitBreaker(IProxyRouteDiagnosticsRoute route, bool wouldProxy)
    {
        ArgumentNullException.ThrowIfNull(route);
        var enabled = route.Upstreams.Any(static upstream => upstream.CircuitBreakerEnabled);
        if (!enabled)
        {
            return Disabled("disabled");
        }

        return wouldProxy ? RouteMatchDryRunPolicy.EnabledAndApplies("configured_for_one_or_more_upstreams") : RouteMatchDryRunPolicy.EnabledButBlocked("not_proxy_action");
    }

    private static bool ContainsHeader(IReadOnlyList<ProxyHeaderField> headers, string name)
    {
        return headers.Any(header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
