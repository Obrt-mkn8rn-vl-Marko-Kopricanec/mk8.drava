namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed class ProxyCacheStatusReader : IProxyCacheStatusReader
{
    private readonly IProxyCacheStatusConfigurationSource _configurationSource;
    private readonly IProxyCacheRuntimeStatusSource _runtimeSource;
    public ProxyCacheStatusReader(IProxyCacheStatusConfigurationSource configurationSource, IProxyCacheRuntimeStatusSource runtimeSource)
    {
        _configurationSource = configurationSource;
        _runtimeSource = runtimeSource;
    }

    public ProxyCacheStatus GetStatus()
    {
        return Project(_configurationSource.ReadRoutes(), _runtimeSource.ReadSnapshot());
    }

    public static ProxyCacheStatus Project(IReadOnlyList<ProxyCacheStatusRouteSource> routes, ProxyCacheRuntimeStatusSnapshot runtime)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(runtime);
        var routeStatuses = routes.Select(route =>
        {
            var currentEntryCount = 0;
            var currentBytes = 0L;
            foreach (var entry in runtime.Entries)
            {
                if (!string.Equals(entry.RouteName, route.RouteName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                currentEntryCount++;
                currentBytes += entry.SizeBytes;
            }

            return ProxyCacheRouteStatus.FromSources(route.RouteName, route.Enabled, route.MaxEntryBytes, route.MaxTotalBytes, currentEntryCount, currentBytes);
        });
        var rejections = runtime.Rejections.OrderBy(static rejection => rejection.Reason, StringComparer.OrdinalIgnoreCase).Select(static rejection => ProxyCacheRejectionStatus.FromSources(rejection.Reason, rejection.Count));
        return ProxyCacheStatus.FromSources(runtime.EntryCount, runtime.ApproximateBytes, runtime.HitCount, runtime.MissCount, runtime.StoreCount, runtime.EvictionCount, runtime.StoreRejectionCount, runtime.LastClearedAtUtc, runtime.LastClearReason, rejections, routeStatuses);
    }
}
