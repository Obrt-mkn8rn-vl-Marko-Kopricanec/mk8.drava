using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public static class ProxyRouteActionKindMapper
{
    public static ProxyRouteActionKind FromRuntimeAction(RuntimeRouteAction action)
    {
        return action switch
        {
            RuntimeRouteAction.Redirect => ProxyRouteActionKind.Redirect,
            RuntimeRouteAction.StaticResponse => ProxyRouteActionKind.StaticResponse,
            _ => ProxyRouteActionKind.Proxy
        };
    }

    public static string FromRuntimeActionText(RuntimeRouteAction action)
    {
        return ToText(FromRuntimeAction(action));
    }

    public static ProxyRouteActionKind FromText(string action)
    {
        return action switch
        {
            _ when string.Equals(action, "Redirect", StringComparison.OrdinalIgnoreCase) => ProxyRouteActionKind.Redirect,
            _ when string.Equals(action, "StaticResponse", StringComparison.OrdinalIgnoreCase) => ProxyRouteActionKind.StaticResponse,
            _ => ProxyRouteActionKind.Proxy
        };
    }

    public static string ToText(ProxyRouteActionKind action)
    {
        return action switch
        {
            ProxyRouteActionKind.Proxy => nameof(ProxyRouteActionKind.Proxy),
            ProxyRouteActionKind.Redirect => nameof(ProxyRouteActionKind.Redirect),
            ProxyRouteActionKind.StaticResponse => nameof(ProxyRouteActionKind.StaticResponse),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown proxy route action kind.")};
    }
}
