namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public interface IProxyConfigurationValidationOperations
{
    ValueTask<ProxyConfigurationValidationResult> ValidateAsync(CancellationToken cancellationToken);
}
