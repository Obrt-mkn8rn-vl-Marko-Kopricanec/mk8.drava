using BusinessProxyConfigurationNormalizeResult = Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationNormalizeResult;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyConfigurationNormalizeResponseMapper
{
    public static ProxyConfigurationNormalizeResponse FromResult(BusinessProxyConfigurationNormalizeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            BusinessProxyConfigurationNormalizeResult.NormalizedResult normalized => new ProxyConfigurationNormalizeResponse(succeeded: true, format: normalized.Format, canonicalJson: normalized.CanonicalJson, errors: normalized.Errors, fileErrors: ProxyConfigurationFileErrorResponseMapper.FromErrors(normalized.FileErrors)),
            BusinessProxyConfigurationNormalizeResult.FailedResult failed => new ProxyConfigurationNormalizeResponse(succeeded: false, format: failed.Format, canonicalJson: null, errors: failed.Errors, fileErrors: ProxyConfigurationFileErrorResponseMapper.FromErrors(failed.FileErrors)),
            _ => throw new InvalidOperationException($"Unknown normalize result '{result.GetType().Name}'.")};
    }
}
