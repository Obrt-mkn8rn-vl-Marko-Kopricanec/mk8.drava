using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;

namespace Mk8.Drava.Application.INF.Configuration;
public sealed class ProxyConfigurationReadProjectionSource : IProxyConfigurationReadProjectionSource<ProxyConfigurationProjection>
{
    private readonly IProxyActiveConfigurationSnapshotReader _configurationStore;
    private readonly IProxyConfigurationHttp3ProjectionSource _http3ProjectionSource;
    public ProxyConfigurationReadProjectionSource(IProxyActiveConfigurationSnapshotReader configurationStore, IProxyConfigurationHttp3ProjectionSource http3ProjectionSource)
    {
        _configurationStore = configurationStore;
        _http3ProjectionSource = http3ProjectionSource;
    }

    public ProxyConfigurationReadProjectionResult<ProxyConfigurationProjection> ReadCurrent()
    {
        return _configurationStore.ReadSnapshot()is ProxyConfigurationSnapshotReadResult.AvailableResult available ? ProxyConfigurationReadProjectionResult<ProxyConfigurationProjection>.Available(ProxyConfigurationProjectionMapper.ToProjection(available.Snapshot, _http3ProjectionSource.Project(ProxyHttp3SupportConfigurationSourceMapper.FromSources(available.Snapshot.Listeners, available.Snapshot.Routes)))) : ProxyConfigurationReadProjectionResult<ProxyConfigurationProjection>.MissingConfiguration;
    }
}
