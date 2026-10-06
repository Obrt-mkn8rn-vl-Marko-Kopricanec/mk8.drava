namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public interface IProxyConfigurationReadOperations<TConfiguration>
    where TConfiguration : class
{
    ProxyConfigurationReadResult<TConfiguration> ReadActive();
    ProxyConfigurationReadResult<TConfiguration> ReadEffective();
}
