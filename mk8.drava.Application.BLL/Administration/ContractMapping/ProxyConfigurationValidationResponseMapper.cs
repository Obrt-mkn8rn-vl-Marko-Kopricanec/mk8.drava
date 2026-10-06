using BusinessProxyConfigurationValidationResult = Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationValidationResult;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyConfigurationValidationResponseMapper
{
    public static ProxyConfigurationValidationResponse FromResult(BusinessProxyConfigurationValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            BusinessProxyConfigurationValidationResult.ValidResult valid => FromResult(valid, succeeded: true),
            BusinessProxyConfigurationValidationResult.InvalidResult invalid => FromResult(invalid, succeeded: false),
            _ => throw new InvalidOperationException($"Unknown validation result '{result.GetType().Name}'.")};
    }

    private static ProxyConfigurationValidationResponse FromResult(BusinessProxyConfigurationValidationResult result, bool succeeded)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ProxyConfigurationValidationResponse(succeeded: succeeded, sourceDirectory: result.SourceDirectory, attemptedAtUtc: result.AttemptedAtUtc, activeVersion: result.ActiveVersion, lastSuccessfulLoadAtUtc: result.LastSuccessfulLoadAtUtc, wouldBeVersion: result.WouldBeVersion, sourceFiles: result.SourceFiles, discovery: ProxyConfigurationDiscoveryResponseMapper.FromDiscovery(result.Discovery), errors: result.Errors, fileErrors: ProxyConfigurationFileErrorResponseMapper.FromErrors(result.FileErrors));
    }
}
