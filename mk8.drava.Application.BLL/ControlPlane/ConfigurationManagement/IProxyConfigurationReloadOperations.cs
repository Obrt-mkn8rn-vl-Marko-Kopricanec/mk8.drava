namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public interface IProxyConfigurationReloadOperations<TProjection>
    where TProjection : class
{
    ValueTask<ProxyConfigurationReloadResult<TProjection>> ReloadAsync(CancellationToken cancellationToken);
}
