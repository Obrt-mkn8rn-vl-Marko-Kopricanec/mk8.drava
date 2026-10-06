namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed record ProxyBackupFileSystemEntry
{
    public ProxyBackupFileSystemEntry(string RelativePath, long SizeBytes, DateTimeOffset LastWriteTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RelativePath);
        ArgumentOutOfRangeException.ThrowIfNegative(SizeBytes);
        this.RelativePath = RelativePath;
        this.SizeBytes = SizeBytes;
        this.LastWriteTimeUtc = LastWriteTimeUtc;
    }

    public string RelativePath { get; }
    public long SizeBytes { get; }
    public DateTimeOffset LastWriteTimeUtc { get; }
}
