namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public sealed class ProxyConfigurationAdministrationService
{
    private readonly IProxyConfigurationNormalizeOperations _normalizeOperations;
    private readonly IProxyConfigurationValidationOperations _validationOperations;
    public ProxyConfigurationAdministrationService(IProxyConfigurationNormalizeOperations normalizeOperations, IProxyConfigurationValidationOperations validationOperations)
    {
        _normalizeOperations = normalizeOperations;
        _validationOperations = validationOperations;
    }

    public ProxyConfigurationNormalizeResult Normalize(ProxyConfigurationNormalizeRequest? request)
    {
        return _normalizeOperations.Normalize(request);
    }

    public ValueTask<ProxyConfigurationValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        return _validationOperations.ValidateAsync(cancellationToken);
    }
}
