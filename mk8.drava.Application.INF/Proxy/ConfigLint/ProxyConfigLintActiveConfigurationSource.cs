using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;

namespace Mk8.Drava.Application.INF.Proxy.ConfigLint;
public sealed class ProxyConfigLintActiveConfigurationSource : IProxyConfigLintActiveConfigurationSource
{
    private readonly IProxyActiveConfigurationSnapshotReader _configurationStore;
    private readonly IRuntimeHttp3PlatformSupportSource _http3PlatformSupportSource;
    public ProxyConfigLintActiveConfigurationSource(IProxyActiveConfigurationSnapshotReader configurationStore, IRuntimeHttp3PlatformSupportSource http3PlatformSupportSource)
    {
        _configurationStore = configurationStore;
        _http3PlatformSupportSource = http3PlatformSupportSource;
    }

    public ProxyConfigLintActiveConfigurationReadResult Read()
    {
        var snapshotResult = _configurationStore.ReadSnapshot();
        if (snapshotResult is not ProxyConfigurationSnapshotReadResult.AvailableResult available)
        {
            return ProxyConfigLintActiveConfigurationReadResult.MissingConfiguration;
        }

        var runtimeSnapshot = available.Snapshot;
        return ProxyConfigLintActiveConfigurationReadResult.Available(ProxyConfigLintConfigurationSnapshotMapper.ToLintSnapshot(ProxyConfigLintRuntimeConfigurationSourceMapper.FromSources(runtimeSnapshot.SourceFiles, runtimeSnapshot.AdminSecurity.Urls, runtimeSnapshot.AdminSecurity.RequireAuthentication, runtimeSnapshot.Metrics.PublicMetricsEnabled, runtimeSnapshot.Listeners, runtimeSnapshot.Routes), _http3PlatformSupportSource.Read()));
    }
}
