using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.DAL.Configuration.Paths;

// The production role uses its validated bootstrap, without the legacy process-global MDRAVA override.
public sealed class ApplicationDataDirectoryProvider : IMdravaDataDirectoryProvider
{
    private readonly string _directory;
    public ApplicationDataDirectoryProvider(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("The Application data directory must be absolute.", nameof(directory));
        _directory = Path.GetFullPath(directory);
    }
    public string GetDataDirectory() => _directory;
    public string GetProxyConfigDirectory() => Path.Combine(_directory, "config");
    public string GetSitesConfigDirectory() => Path.Combine(GetProxyConfigDirectory(), "sites");
    public string GetProxyOperationalConfigPath() => Path.Combine(GetProxyConfigDirectory(), "proxy.json");
    public string GetLogsDirectory() => Path.Combine(_directory, "logs");
    public string GetCertificatesDirectory() => Path.Combine(_directory, "certs");
    public string GetStateDirectory() => Path.Combine(_directory, "state");
}
