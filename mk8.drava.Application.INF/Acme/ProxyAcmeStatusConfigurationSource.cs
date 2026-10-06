using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;

namespace Mk8.Drava.Application.INF.Acme;
public sealed class ProxyAcmeStatusConfigurationSource : IProxyAcmeStatusConfigurationSource
{
    private readonly IProxyActiveConfigurationSnapshotReader _configurationStore;
    public ProxyAcmeStatusConfigurationSource(IProxyActiveConfigurationSnapshotReader configurationStore)
    {
        _configurationStore = configurationStore;
    }

    public ProxyAcmeStatusConfigurationSourceReadResult Read()
    {
        var snapshotResult = _configurationStore.ReadSnapshot();
        if (snapshotResult is not ProxyConfigurationSnapshotReadResult.AvailableResult available)
        {
            return ProxyAcmeStatusConfigurationSourceReadResult.MissingConfiguration;
        }

        return ProxyAcmeStatusConfigurationSourceReadResult.Available(ProxyAcmeStatusConfigurationSourceMapper.FromSources(available.Snapshot.Acme, available.Snapshot.Certificates));
    }
}
