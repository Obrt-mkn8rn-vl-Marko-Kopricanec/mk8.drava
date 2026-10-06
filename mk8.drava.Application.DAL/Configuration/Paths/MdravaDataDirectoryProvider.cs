using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.DAL.Configuration.Paths;
public sealed class MdravaDataDirectoryProvider : IMdravaDataDirectoryProvider
{
    public const string EnvironmentVariableName = "MDRAVA_DATA_DIR";
    private readonly MdravaDataDirectoryOptions _options;
    public MdravaDataDirectoryProvider(MdravaDataDirectoryOptions options)
    {
        _options = options;
    }

    public string GetDataDirectory()
    {
        var environmentOverride = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentOverride))
        {
            return Path.GetFullPath(environmentOverride);
        }

        if (!string.IsNullOrWhiteSpace(_options.DataDirectory))
        {
            return Path.GetFullPath(_options.DataDirectory);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "MDRAVA");
        }

        return Path.Combine(AppContext.BaseDirectory, "MDRAVA");
    }

    public string GetProxyConfigDirectory()
    {
        return Path.Combine(GetDataDirectory(), "config");
    }

    public string GetSitesConfigDirectory()
    {
        return Path.Combine(GetProxyConfigDirectory(), "sites");
    }

    public string GetProxyOperationalConfigPath()
    {
        return Path.Combine(GetProxyConfigDirectory(), "proxy.json");
    }

    public string GetLogsDirectory()
    {
        return Path.Combine(GetDataDirectory(), "logs");
    }

    public string GetCertificatesDirectory()
    {
        return Path.Combine(GetDataDirectory(), "certs");
    }

    public string GetStateDirectory()
    {
        return Path.Combine(GetDataDirectory(), "state");
    }
}
