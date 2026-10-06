using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;

namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public static class ProxyRouteActionRuntimeMapper
{
    public static ProxyRouteActionInput ToPolicyInput(RuntimeRoute route, Http1RequestHead requestHead, RuntimeListener listener, bool isUpgradeRequest)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(requestHead);
        ArgumentNullException.ThrowIfNull(listener);
        return new ProxyRouteActionInput(ProxyRouteActionKindMapper.FromRuntimeAction(route.Action), new ProxyRoutePolicyRedirectInput(route.HttpsRedirect.Enabled, route.HttpsRedirect.StatusCode, route.HttpsRedirect.HttpsPort, route.CanonicalHost.Enabled, route.CanonicalHost.TargetHost, route.CanonicalHost.StatusCode, RuntimeListenerTransportScheme.FromTransport(listener.Transport), requestHead.Host, requestHead.Target), new ProxyRouteMaintenanceActionInput(route.Maintenance.Enabled, route.Maintenance.RetryAfterSeconds, route.Maintenance.ContentType, route.Maintenance.Body), new ProxyRouteRedirectActionInput(route.Redirect.StatusCode, route.Redirect.TargetUrl, route.Redirect.TargetPath, route.Redirect.PreserveQuery), new ProxyRouteStaticResponseActionInput(route.StaticResponse.StatusCode, route.StaticResponse.ContentType, route.StaticResponse.Body), isUpgradeRequest);
    }
}
