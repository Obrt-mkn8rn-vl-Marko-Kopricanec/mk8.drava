namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public static class ProxyRestoreValidationResultBuilder
{
    public static ProxyRestoreValidationResult Build(DateTimeOffset generatedAtUtc, int? activeConfigVersion, ProxyRestoreConfigurationValidationResult configValidation, ProxyBackupManifest manifest, IEnumerable<ProxyRestoreValidationFinding> errors, IEnumerable<ProxyRestoreValidationFinding> warnings, int maxFindings)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxFindings);
        return ProxyRestoreValidationResult.Completed(generatedAtUtc, activeConfigVersion, configValidation, manifest, errors.Take(maxFindings), warnings.Take(maxFindings));
    }
}
