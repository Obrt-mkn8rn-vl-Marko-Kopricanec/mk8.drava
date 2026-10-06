using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public static class ProxyCacheStatusRouteSourceMapper
{
    public static IReadOnlyList<ProxyCacheStatusRouteSource> ToRouteSources(IEnumerable<RuntimeRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        return CacheList.Copy(routes.Select(ToRouteSource));
    }

    private static ProxyCacheStatusRouteSource ToRouteSource(RuntimeRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return new ProxyCacheStatusRouteSource(route.Name, route.Cache.Enabled, route.Cache.MaxEntryBytes, route.Cache.MaxTotalBytes);
    }
}
