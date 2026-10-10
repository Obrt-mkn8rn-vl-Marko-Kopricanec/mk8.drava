using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
public static class ProxyTimeoutRuntimeMapper
{
    public static ProxyRouteTimeoutPolicyInput ToPolicyInput(RuntimeRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return new ProxyRouteTimeoutPolicyInput(route.ResolvedOptions.UpstreamResponseHeadTimeout, route.Retry.PerAttemptTimeout)
        {
            FlowTimeouts = route.ResolvedOptions.FlowTimeouts,
        };
    }
}
