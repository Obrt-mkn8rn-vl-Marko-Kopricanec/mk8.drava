namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;

public sealed class ProxyRouteDiagnosticsActionPolicyAdapter : IProxyRouteDiagnosticsActionPolicy
{
    private readonly ProxyRouteActionPolicy _policy = new();
    public ProxyRouteDiagnosticsActionDecision Evaluate(IProxyRouteDiagnosticsRoute route, ProxyRouteDiagnosticsRequestHead requestHead, IProxyRouteDiagnosticsListener listener, bool isUpgradeRequest)
    {
        ArgumentNullException.ThrowIfNull(requestHead);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(listener);
        var decision = _policy.Evaluate(ToPolicyInput(route, requestHead, listener, isUpgradeRequest));
        if (decision.Response is not null)
        {
            return ProxyRouteDiagnosticsActionDecision.GeneratedResponse(decision.Response.StatusCode);
        }

        return ProxyRouteDiagnosticsActionDecision.Proxy;
    }

    private static ProxyRouteActionInput ToPolicyInput(IProxyRouteDiagnosticsRoute route, ProxyRouteDiagnosticsRequestHead requestHead, IProxyRouteDiagnosticsListener listener, bool isUpgradeRequest)
    {
        return new ProxyRouteActionInput(ProxyRouteActionKindMapper.FromText(route.Action), new ProxyRoutePolicyRedirectInput(route.HttpsRedirect.Enabled, route.HttpsRedirect.StatusCode, route.HttpsRedirect.HttpsPort, route.CanonicalHost.Enabled, route.CanonicalHost.TargetHost, route.CanonicalHost.StatusCode, listener.Transport, requestHead.Host, requestHead.Target), new ProxyRouteMaintenanceActionInput(route.Maintenance.Enabled, route.Maintenance.RetryAfterSeconds, route.Maintenance.ContentType, route.Maintenance.Body), new ProxyRouteRedirectActionInput(route.Redirect.StatusCode, route.Redirect.TargetUrl, route.Redirect.TargetPath, route.Redirect.PreserveQuery), new ProxyRouteStaticResponseActionInput(route.StaticResponse.StatusCode, route.StaticResponse.ContentType, route.StaticResponse.Body), isUpgradeRequest);
    }
}
