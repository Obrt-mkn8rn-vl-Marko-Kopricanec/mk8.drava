namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyFilesystemLayoutResponse(string DataDirectory, string ConfigDirectory, string SitesDirectory, string LogsDirectory, string CertificatesDirectory, string StateDirectory, string ProxyConfigPath)
{
}
