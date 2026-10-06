namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public sealed class ProxyConfigurationReadAdministrationService<TConfiguration>
    where TConfiguration : class
{
    private readonly IProxyConfigurationReadOperations<TConfiguration> _readOperations;
    public ProxyConfigurationReadAdministrationService(IProxyConfigurationReadOperations<TConfiguration> readOperations)
    {
        _readOperations = readOperations;
    }

    public ProxyConfigurationReadResult<TConfiguration> ReadActive()
    {
        return _readOperations.ReadActive();
    }

    public ProxyConfigurationReadResult<TConfiguration> ReadEffective()
    {
        return _readOperations.ReadEffective();
    }
}
