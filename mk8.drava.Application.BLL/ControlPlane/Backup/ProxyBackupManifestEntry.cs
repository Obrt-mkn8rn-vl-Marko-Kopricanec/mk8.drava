namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed record ProxyBackupManifestEntry
{
    public ProxyBackupManifestEntry(string RelativePath, string Category, string Classification, bool Sensitive, long SizeBytes, DateTimeOffset LastWriteTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RelativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(Classification);
        ArgumentOutOfRangeException.ThrowIfNegative(SizeBytes);
        this.RelativePath = RelativePath;
        this.Category = Category;
        this.Classification = Classification;
        this.Sensitive = Sensitive;
        this.SizeBytes = SizeBytes;
        this.LastWriteTimeUtc = LastWriteTimeUtc;
    }

    public string RelativePath { get; }
    public string Category { get; }
    public string Classification { get; }
    public bool Sensitive { get; }
    public long SizeBytes { get; }
    public DateTimeOffset LastWriteTimeUtc { get; }
}
