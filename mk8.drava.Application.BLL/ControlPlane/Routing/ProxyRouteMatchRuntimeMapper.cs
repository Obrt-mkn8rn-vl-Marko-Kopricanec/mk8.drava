using System.Collections.ObjectModel;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;

namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public static class ProxyRouteMatchRuntimeMapper
{
    public static IReadOnlyList<RouteMatchCandidate> ToCandidates(IEnumerable<RuntimeRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        return new ReadOnlyCollection<RouteMatchCandidate>(routes.Select(ToCandidate).ToArray());
    }

    public static RouteMatchRequest ToRequest(Http1RequestHead requestHead)
    {
        ArgumentNullException.ThrowIfNull(requestHead);
        return new RouteMatchRequest(requestHead.Host, requestHead.Path);
    }

    public static RuntimeRoute SelectRoute(IReadOnlyList<RuntimeRoute> routes, RouteMatch match)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(match);
        return routes[match.RouteIndex];
    }

    private static RouteMatchCandidate ToCandidate(RuntimeRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return new RouteMatchCandidate(route.Host, route.PathPrefix);
    }
}
