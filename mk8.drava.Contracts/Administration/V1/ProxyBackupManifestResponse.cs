namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyBackupManifestResponse
{
    public ProxyBackupManifestResponse(DateTimeOffset generatedAtUtc, IReadOnlyList<ProxyBackupDirectoryStatusResponse> directories, IReadOnlyList<ProxyBackupManifestEntryResponse> entries, IReadOnlyList<ProxyBackupManifestCountResponse> counts, IReadOnlyList<ProxyBackupWarningResponse> warnings, bool truncated)
    {
        GeneratedAtUtc = generatedAtUtc;
        Directories = ApiResponseList.Copy(directories);
        Entries = ApiResponseList.Copy(entries);
        Counts = ApiResponseList.Copy(counts);
        Warnings = ApiResponseList.Copy(warnings);
        Truncated = truncated;
    }

    public DateTimeOffset GeneratedAtUtc { get; }
    public IReadOnlyList<ProxyBackupDirectoryStatusResponse> Directories { get; }
    public IReadOnlyList<ProxyBackupManifestEntryResponse> Entries { get; }
    public IReadOnlyList<ProxyBackupManifestCountResponse> Counts { get; }
    public IReadOnlyList<ProxyBackupWarningResponse> Warnings { get; }
    public bool Truncated { get; }
}
