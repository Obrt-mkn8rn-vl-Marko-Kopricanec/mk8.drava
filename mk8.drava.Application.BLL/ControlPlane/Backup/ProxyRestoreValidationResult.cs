namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public abstract record ProxyRestoreValidationResult
{
    private ProxyRestoreValidationResult(DateTimeOffset generatedAtUtc, int? activeConfigVersion, ProxyRestoreConfigurationValidationResult configValidation, ProxyBackupManifest manifest, IEnumerable<ProxyRestoreValidationFinding> errors, IEnumerable<ProxyRestoreValidationFinding> warnings)
    {
        ArgumentNullException.ThrowIfNull(configValidation);
        ArgumentNullException.ThrowIfNull(manifest);
        ThrowIfNonPositive(activeConfigVersion, nameof(activeConfigVersion));
        GeneratedAtUtc = generatedAtUtc;
        ActiveConfigVersion = activeConfigVersion;
        ConfigValidationSucceeded = configValidation is ProxyRestoreConfigurationValidationResult.ValidResult;
        WouldBeConfigVersion = configValidation.WouldBeVersion;
        Manifest = manifest;
        Errors = BackupList.Copy(errors);
        Warnings = BackupList.Copy(warnings);
    }

    public DateTimeOffset GeneratedAtUtc { get; }
    public int? ActiveConfigVersion { get; }
    public bool ConfigValidationSucceeded { get; }
    public int? WouldBeConfigVersion { get; }
    public ProxyBackupManifest Manifest { get; }
    public IReadOnlyList<ProxyRestoreValidationFinding> Errors { get; }
    public IReadOnlyList<ProxyRestoreValidationFinding> Warnings { get; }

    public static ProxyRestoreValidationResult Completed(DateTimeOffset generatedAtUtc, int? activeConfigVersion, ProxyRestoreConfigurationValidationResult configValidation, ProxyBackupManifest manifest, IEnumerable<ProxyRestoreValidationFinding> errors, IEnumerable<ProxyRestoreValidationFinding> warnings)
    {
        var ownedErrors = BackupList.Copy(errors);
        return configValidation is ProxyRestoreConfigurationValidationResult.ValidResult && ownedErrors.Count == 0 ? new AcceptedResult(generatedAtUtc, activeConfigVersion, configValidation, manifest, ownedErrors, warnings) : new RejectedResult(generatedAtUtc, activeConfigVersion, configValidation, manifest, ownedErrors, warnings);
    }

    private static void ThrowIfNonPositive(int? value, string paramName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedResult : ProxyRestoreValidationResult
    {
        internal AcceptedResult(DateTimeOffset generatedAtUtc, int? activeConfigVersion, ProxyRestoreConfigurationValidationResult configValidation, ProxyBackupManifest manifest, IEnumerable<ProxyRestoreValidationFinding> errors, IEnumerable<ProxyRestoreValidationFinding> warnings) : base(generatedAtUtc, activeConfigVersion, configValidation, manifest, errors, warnings)
        {
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedResult : ProxyRestoreValidationResult
    {
        internal RejectedResult(DateTimeOffset generatedAtUtc, int? activeConfigVersion, ProxyRestoreConfigurationValidationResult configValidation, ProxyBackupManifest manifest, IEnumerable<ProxyRestoreValidationFinding> errors, IEnumerable<ProxyRestoreValidationFinding> warnings) : base(generatedAtUtc, activeConfigVersion, configValidation, manifest, errors, warnings)
        {
        }
    }
}
