namespace Mk8.Drava.Application.BLL.Configuration;
public interface IMdravaDataDirectoryProvider
{
    string GetDataDirectory();
    string GetProxyConfigDirectory();
    string GetProxyOperationalConfigPath();
    string GetSitesConfigDirectory();
    string GetLogsDirectory();
    string GetCertificatesDirectory();
    string GetStateDirectory();
}
