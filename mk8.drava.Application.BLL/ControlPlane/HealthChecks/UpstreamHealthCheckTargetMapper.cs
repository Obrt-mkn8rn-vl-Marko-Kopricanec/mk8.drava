using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;

namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public static class UpstreamHealthCheckTargetMapper
{
    public static IReadOnlyList<UpstreamHealthCheckTarget> FromRoutes(IEnumerable<RuntimeRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        return HealthCheckList.Copy(routes.Where(static route =>
        {
            ArgumentNullException.ThrowIfNull(route);
            return route.HealthCheck.Enabled;
        }).SelectMany(static route => route.Upstreams.Select(upstream => ToTarget(route, upstream))));
    }

    private static UpstreamHealthCheckTarget ToTarget(RuntimeRoute route, RuntimeUpstream upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        return new UpstreamHealthCheckTarget(route.Name, upstream.Name, upstream.Endpoint, upstream.Identity, UpstreamTransportEndpointMapper.FromUpstream(upstream), route.HealthCheck.Path, route.HealthCheck.Interval, route.HealthCheck.Timeout, route.HealthCheck.HealthyThreshold, route.HealthCheck.UnhealthyThreshold);
    }
}
