namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyRestoreValidationResponseBody
{
    public ProxyRestoreValidationResponseBody(bool succeeded, DateTimeOffset generatedAtUtc, int? activeConfigVersion, bool configValidationSucceeded, int? wouldBeConfigVersion, ProxyBackupManifestResponse manifest, IReadOnlyList<ProxyRestoreValidationFindingResponse> errors, IReadOnlyList<ProxyRestoreValidationFindingResponse> warnings)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Succeeded = succeeded;
        GeneratedAtUtc = generatedAtUtc;
        ActiveConfigVersion = activeConfigVersion;
        ConfigValidationSucceeded = configValidationSucceeded;
        WouldBeConfigVersion = wouldBeConfigVersion;
        Manifest = manifest;
        Errors = ApiResponseList.Copy(errors);
        Warnings = ApiResponseList.Copy(warnings);
    }

    public bool Succeeded { get; }
    public DateTimeOffset GeneratedAtUtc { get; }
    public int? ActiveConfigVersion { get; }
    public bool ConfigValidationSucceeded { get; }
    public int? WouldBeConfigVersion { get; }
    public ProxyBackupManifestResponse Manifest { get; }
    public IReadOnlyList<ProxyRestoreValidationFindingResponse> Errors { get; }
    public IReadOnlyList<ProxyRestoreValidationFindingResponse> Warnings { get; }
}
