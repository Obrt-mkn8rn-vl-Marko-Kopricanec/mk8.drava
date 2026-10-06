namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record ProxyFilesystemLayout
{
    public ProxyFilesystemLayout(string DataDirectory, string ConfigDirectory, string SitesDirectory, string LogsDirectory, string CertificatesDirectory, string StateDirectory, string ProxyConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(ConfigDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(SitesDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(LogsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(CertificatesDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(StateDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(ProxyConfigPath);
        this.DataDirectory = DataDirectory;
        this.ConfigDirectory = ConfigDirectory;
        this.SitesDirectory = SitesDirectory;
        this.LogsDirectory = LogsDirectory;
        this.CertificatesDirectory = CertificatesDirectory;
        this.StateDirectory = StateDirectory;
        this.ProxyConfigPath = ProxyConfigPath;
    }

    public string DataDirectory { get; }
    public string ConfigDirectory { get; }
    public string SitesDirectory { get; }
    public string LogsDirectory { get; }
    public string CertificatesDirectory { get; }
    public string StateDirectory { get; }
    public string ProxyConfigPath { get; }
}
