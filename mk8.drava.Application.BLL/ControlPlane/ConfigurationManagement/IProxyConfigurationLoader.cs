namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public interface IProxyConfigurationLoader
{
    ValueTask<ProxyConfigurationLoadResult> LoadAsync(CancellationToken cancellationToken);
    ValueTask<ProxyConfigurationLoadResult> ValidateAsync(CancellationToken cancellationToken);
}
