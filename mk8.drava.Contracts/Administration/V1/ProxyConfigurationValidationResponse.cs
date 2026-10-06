namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyConfigurationValidationResponse
{
    public ProxyConfigurationValidationResponse(bool succeeded, string sourceDirectory, DateTimeOffset attemptedAtUtc, int? activeVersion, DateTimeOffset? lastSuccessfulLoadAtUtc, int? wouldBeVersion, IReadOnlyList<string> sourceFiles, ProxyConfigurationDiscoveryResponse discovery, IReadOnlyList<string> errors, IReadOnlyList<ProxyConfigurationFileErrorResponse> fileErrors)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        Succeeded = succeeded;
        SourceDirectory = sourceDirectory;
        AttemptedAtUtc = attemptedAtUtc;
        ActiveVersion = activeVersion;
        LastSuccessfulLoadAtUtc = lastSuccessfulLoadAtUtc;
        WouldBeVersion = wouldBeVersion;
        SourceFiles = ApiResponseList.Copy(sourceFiles);
        Discovery = discovery;
        Errors = ApiResponseList.Copy(errors);
        FileErrors = ApiResponseList.Copy(fileErrors);
    }

    public bool Succeeded { get; }
    public string SourceDirectory { get; }
    public DateTimeOffset AttemptedAtUtc { get; }
    public int? ActiveVersion { get; }
    public DateTimeOffset? LastSuccessfulLoadAtUtc { get; }
    public int? WouldBeVersion { get; }
    public IReadOnlyList<string> SourceFiles { get; }
    public ProxyConfigurationDiscoveryResponse Discovery { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<ProxyConfigurationFileErrorResponse> FileErrors { get; }
}
