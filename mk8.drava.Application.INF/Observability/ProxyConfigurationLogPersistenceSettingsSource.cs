using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.Observability;

namespace Mk8.Drava.Application.INF.Observability;
public sealed class ProxyConfigurationLogPersistenceSettingsSource : IProxyLogPersistenceSettingsSource
{
    private readonly IProxyActiveConfigurationSnapshotReader _configurationStore;
    public ProxyConfigurationLogPersistenceSettingsSource(IProxyActiveConfigurationSnapshotReader configurationStore)
    {
        _configurationStore = configurationStore;
    }

    public ProxyLogPersistenceSettingsSourceResult ReadLogPersistenceSettings()
    {
        if (_configurationStore.ReadSnapshot()is ProxyConfigurationSnapshotReadResult.AvailableResult available)
        {
            return ProxyLogPersistenceSettingsSourceResult.Available(ProxyLogPersistenceSettingsMapper.FromSource(available.Snapshot.Observability.LogPersistence));
        }

        return ProxyLogPersistenceSettingsSourceResult.MissingConfiguration;
    }
}
