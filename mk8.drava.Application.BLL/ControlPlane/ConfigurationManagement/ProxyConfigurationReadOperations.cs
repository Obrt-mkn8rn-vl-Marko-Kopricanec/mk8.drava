namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public sealed class ProxyConfigurationReadOperations<TConfiguration> : IProxyConfigurationReadOperations<TConfiguration> where TConfiguration : class
{
    private readonly IProxyConfigurationReadProjectionSource<TConfiguration> _projectionSource;
    public ProxyConfigurationReadOperations(IProxyConfigurationReadProjectionSource<TConfiguration> projectionSource)
    {
        _projectionSource = projectionSource;
    }

    public ProxyConfigurationReadResult<TConfiguration> ReadActive()
    {
        return ReadCurrent();
    }

    public ProxyConfigurationReadResult<TConfiguration> ReadEffective()
    {
        return ReadCurrent();
    }

    private ProxyConfigurationReadResult<TConfiguration> ReadCurrent()
    {
        return _projectionSource.ReadCurrent()is ProxyConfigurationReadProjectionResult<TConfiguration>.AvailableResult available ? ProxyConfigurationReadResult<TConfiguration>.Available(available.Configuration) : ProxyConfigurationReadResult<TConfiguration>.MissingConfiguration;
    }
}
