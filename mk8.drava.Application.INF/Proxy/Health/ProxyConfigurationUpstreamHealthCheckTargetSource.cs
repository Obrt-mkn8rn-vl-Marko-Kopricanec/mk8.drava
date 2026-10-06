using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;

namespace Mk8.Drava.Application.INF.Proxy.Health;
public sealed class ProxyConfigurationUpstreamHealthCheckTargetSource : IUpstreamHealthCheckTargetSource
{
    private readonly IProxyActiveConfigurationSnapshotReader _configurationStore;
    public ProxyConfigurationUpstreamHealthCheckTargetSource(IProxyActiveConfigurationSnapshotReader configurationStore)
    {
        _configurationStore = configurationStore;
    }

    public IReadOnlyList<UpstreamHealthCheckTarget> ReadTargets()
    {
        return _configurationStore.ReadSnapshot()is ProxyConfigurationSnapshotReadResult.AvailableResult available ? UpstreamHealthCheckTargetMapper.FromRoutes(available.Snapshot.Routes) : [];
    }
}
