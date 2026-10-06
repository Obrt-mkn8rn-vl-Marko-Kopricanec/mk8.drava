using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;

namespace Mk8.Drava.Application.INF.Acme;
public sealed class ProxyConfigurationAcmeRenewalConfigurationSource : IAcmeRenewalConfigurationSource
{
    private readonly IProxyActiveConfigurationSnapshotReader _configurationStore;
    public ProxyConfigurationAcmeRenewalConfigurationSource(IProxyActiveConfigurationSnapshotReader configurationStore)
    {
        _configurationStore = configurationStore;
    }

    public AcmeRenewalConfigurationInputReadResult ReadInput()
    {
        var snapshotResult = _configurationStore.ReadSnapshot();
        if (snapshotResult is not ProxyConfigurationSnapshotReadResult.AvailableResult available)
        {
            return AcmeRenewalConfigurationInputReadResult.MissingConfiguration;
        }

        var snapshot = available.Snapshot;
        return AcmeRenewalConfigurationInputReadResult.Available(AcmeRenewalConfigurationInputMapper.FromSources(AcmeRenewalConfigurationSourceMapper.FromSources(snapshot.Acme, snapshot.Certificates)));
    }
}
