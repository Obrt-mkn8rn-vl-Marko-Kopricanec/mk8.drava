namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed record ProxyBackupManifestCount
{
    public ProxyBackupManifestCount(string Category, string Classification, int Count, long SizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(Classification);
        ArgumentOutOfRangeException.ThrowIfNegative(Count);
        ArgumentOutOfRangeException.ThrowIfNegative(SizeBytes);
        this.Category = Category;
        this.Classification = Classification;
        this.Count = Count;
        this.SizeBytes = SizeBytes;
    }

    public string Category { get; }
    public string Classification { get; }
    public int Count { get; }
    public long SizeBytes { get; }
}
