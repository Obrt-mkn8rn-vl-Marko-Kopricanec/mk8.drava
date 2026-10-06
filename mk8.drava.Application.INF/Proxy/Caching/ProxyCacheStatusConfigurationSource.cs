using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;

namespace Mk8.Drava.Application.INF.Proxy.Caching;
public sealed class ProxyCacheStatusConfigurationSource : IProxyCacheStatusConfigurationSource
{
    private readonly IProxyActiveConfigurationSnapshotReader _configurationStore;
    public ProxyCacheStatusConfigurationSource(IProxyActiveConfigurationSnapshotReader configurationStore)
    {
        _configurationStore = configurationStore;
    }

    public IReadOnlyList<ProxyCacheStatusRouteSource> ReadRoutes()
    {
        return _configurationStore.ReadSnapshot()is ProxyConfigurationSnapshotReadResult.AvailableResult available ? ProxyCacheStatusRouteSourceMapper.ToRouteSources(available.Snapshot.Routes) : [];
    }
}
