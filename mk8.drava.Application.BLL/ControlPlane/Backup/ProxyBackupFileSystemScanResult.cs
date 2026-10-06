namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed record ProxyBackupFileSystemScanResult
{
    private ProxyBackupFileSystemScanResult(bool rootExists, IEnumerable<ProxyBackupFileSystemEntry> files, IEnumerable<ProxyBackupFileSystemWarning> warnings)
    {
        RootExists = rootExists;
        Files = BackupList.Copy(files);
        Warnings = BackupList.Copy(warnings);
    }

    public bool RootExists { get; }
    public IReadOnlyList<ProxyBackupFileSystemEntry> Files { get; }
    public IReadOnlyList<ProxyBackupFileSystemWarning> Warnings { get; }

    public static ProxyBackupFileSystemScanResult MissingRoot()
    {
        return new ProxyBackupFileSystemScanResult(rootExists: false, files: [], warnings: []);
    }

    public static ProxyBackupFileSystemScanResult Scanned(IEnumerable<ProxyBackupFileSystemEntry> files, IEnumerable<ProxyBackupFileSystemWarning> warnings)
    {
        return new ProxyBackupFileSystemScanResult(rootExists: true, files: files, warnings: warnings);
    }
}
