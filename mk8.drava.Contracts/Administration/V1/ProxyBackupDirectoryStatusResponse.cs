namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyBackupDirectoryStatusResponse(string RelativePath, bool Exists, string Classification, bool Sensitive)
{
}
