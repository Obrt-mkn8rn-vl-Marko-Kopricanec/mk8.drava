using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyRouteSummarySourceMapper
{
    public static IReadOnlyList<ProxyRouteSummarySource> FromRoutes(IEnumerable<RuntimeRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        return ProxyStatusList.Copy(routes.Select(ToSource));
    }

    private static ProxyRouteSummarySource ToSource(RuntimeRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return new ProxyRouteSummarySource(route.SiteName, route.Action == RuntimeRouteAction.Proxy, route.Cache.Enabled, route.Upstreams.Any(static upstream =>
        {
            ArgumentNullException.ThrowIfNull(upstream);
            return RuntimeUpstreamProtocol.IsHttp3(upstream.Protocol);
        }));
    }
}
