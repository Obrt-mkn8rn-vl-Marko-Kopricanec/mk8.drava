using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;

namespace Mk8.Drava.Application.INF.Proxy.RouteDiagnostics;
public sealed class ProxyRouteDiagnosticsConfigurationSource : IProxyRouteDiagnosticsConfigurationSource
{
    private readonly IProxyActiveConfigurationSnapshotReader _configurationStore;
    public ProxyRouteDiagnosticsConfigurationSource(IProxyActiveConfigurationSnapshotReader configurationStore)
    {
        _configurationStore = configurationStore;
    }

    public ProxyRouteDiagnosticsConfigurationReadResult Read()
    {
        var snapshotResult = _configurationStore.ReadSnapshot();
        if (snapshotResult is not ProxyConfigurationSnapshotReadResult.AvailableResult available)
        {
            return ProxyRouteDiagnosticsConfigurationReadResult.MissingConfiguration;
        }

        return ProxyRouteDiagnosticsConfigurationReadResult.Available(ProxyRouteDiagnosticsRuntimeConfigurationSnapshotMapper.FromSources(available.Snapshot.Listeners, available.Snapshot.Routes));
    }
}
