namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyBackupManifestEntryResponse(string RelativePath, string Category, string Classification, bool Sensitive, long SizeBytes, DateTimeOffset LastWriteTimeUtc)
{
}
