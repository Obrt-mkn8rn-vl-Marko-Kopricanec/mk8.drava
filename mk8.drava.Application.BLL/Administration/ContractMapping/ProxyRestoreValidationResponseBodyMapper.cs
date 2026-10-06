using BusinessProxyRestoreValidationFinding = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyRestoreValidationFinding;
using BusinessProxyRestoreValidationResult = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyRestoreValidationResult;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyRestoreValidationResponseBodyMapper
{
    public static ProxyRestoreValidationResponseBody FromResult(BusinessProxyRestoreValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            BusinessProxyRestoreValidationResult.AcceptedResult accepted => FromResult(accepted, succeeded: true),
            BusinessProxyRestoreValidationResult.RejectedResult rejected => FromResult(rejected, succeeded: false),
            _ => throw new InvalidOperationException($"Unknown restore validation result '{result.GetType().Name}'.")};
    }

    private static ProxyRestoreValidationResponseBody FromResult(BusinessProxyRestoreValidationResult result, bool succeeded)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ProxyRestoreValidationResponseBody(succeeded: succeeded, generatedAtUtc: result.GeneratedAtUtc, activeConfigVersion: result.ActiveConfigVersion, configValidationSucceeded: result.ConfigValidationSucceeded, wouldBeConfigVersion: result.WouldBeConfigVersion, manifest: ProxyBackupManifestResponseMapper.FromManifest(result.Manifest), errors: ProxyRestoreValidationFindingResponseMapper.FromFindings(result.Errors), warnings: ProxyRestoreValidationFindingResponseMapper.FromFindings(result.Warnings));
    }
}
