namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public interface IProxyConfigurationReadProjectionSource<TConfiguration>
    where TConfiguration : class
{
    ProxyConfigurationReadProjectionResult<TConfiguration> ReadCurrent();
}
