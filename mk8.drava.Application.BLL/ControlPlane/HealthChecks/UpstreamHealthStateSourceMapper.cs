using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;

namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public static class UpstreamHealthStateSourceMapper
{
    public static UpstreamHealthStateSource FromUpstream(RuntimeUpstream upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        return new UpstreamHealthStateSource(upstream.Identity, upstream.RouteName, upstream.Name, upstream.Endpoint);
    }
}
