namespace Mk8.Drava.Application.BLL.Configuration;

public interface IProxyManualConfigurationSource
{
    ValueTask<ProxyManualConfigurationInput> ReadAsync(CancellationToken cancellationToken);
}
