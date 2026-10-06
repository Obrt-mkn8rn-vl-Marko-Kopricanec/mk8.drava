using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
public static class ProxyUpstreamSelectionRuntimeMapper
{
    public static UpstreamSelectionRoute ToSelectionRoute(RuntimeRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return new UpstreamSelectionRoute(route.Name, route.HealthCheck.Enabled, route.Upstreams) { Policy = route.Balancing ?? UpstreamBalancingPolicy.FromName(route.LoadBalancingPolicy) };
    }
}
