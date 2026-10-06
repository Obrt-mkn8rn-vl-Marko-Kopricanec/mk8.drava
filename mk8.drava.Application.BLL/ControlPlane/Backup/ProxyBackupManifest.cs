namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed record ProxyBackupManifest
{
    public ProxyBackupManifest(DateTimeOffset GeneratedAtUtc, IEnumerable<ProxyBackupDirectoryStatus> Directories, IEnumerable<ProxyBackupManifestEntry> Entries, IEnumerable<ProxyBackupManifestCount> Counts, IEnumerable<ProxyBackupWarning> Warnings, bool Truncated)
    {
        this.GeneratedAtUtc = GeneratedAtUtc;
        this.Directories = BackupList.Copy(Directories);
        this.Entries = BackupList.Copy(Entries);
        this.Counts = BackupList.Copy(Counts);
        this.Warnings = BackupList.Copy(Warnings);
        this.Truncated = Truncated;
    }

    public DateTimeOffset GeneratedAtUtc { get; }
    public IReadOnlyList<ProxyBackupDirectoryStatus> Directories { get; }
    public IReadOnlyList<ProxyBackupManifestEntry> Entries { get; }
    public IReadOnlyList<ProxyBackupManifestCount> Counts { get; }
    public IReadOnlyList<ProxyBackupWarning> Warnings { get; }
    public bool Truncated { get; }
}
