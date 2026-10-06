using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
public static class ProxyRequestContextRuntimeMapper
{
    public static string ToTransport(RuntimeListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return RuntimeListenerTransportText.FromTransport(listener.Transport);
    }

    public static ProxyRequestRoute ToRequestRoute(RuntimeRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return new ProxyRequestRoute(route.Name, route.SiteName, route.Maintenance.Enabled ? "maintenance" : ActionName(route.Action), route.ResolvedOptions.AccessLogEnabled);
    }

    public static ProxyRequestUpstream ToRequestUpstream(RuntimeUpstream upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        return new ProxyRequestUpstream(upstream.Name, upstream.Endpoint);
    }

    private static string ActionName(RuntimeRouteAction action)
    {
        return action switch
        {
            RuntimeRouteAction.Redirect => "redirect",
            RuntimeRouteAction.StaticResponse => "staticResponse",
            _ => "proxy"
        };
    }
}
