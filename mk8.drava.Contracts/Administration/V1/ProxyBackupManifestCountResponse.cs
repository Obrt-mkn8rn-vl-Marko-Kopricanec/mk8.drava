namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyBackupManifestCountResponse(string Category, string Classification, int Count, long SizeBytes)
{
}
