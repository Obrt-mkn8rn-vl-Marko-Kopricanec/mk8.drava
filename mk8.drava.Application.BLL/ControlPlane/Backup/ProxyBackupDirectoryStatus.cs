namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed record ProxyBackupDirectoryStatus
{
    public ProxyBackupDirectoryStatus(string RelativePath, bool Exists, string Classification, bool Sensitive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RelativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(Classification);
        this.RelativePath = RelativePath;
        this.Exists = Exists;
        this.Classification = Classification;
        this.Sensitive = Sensitive;
    }

    public string RelativePath { get; }
    public bool Exists { get; }
    public string Classification { get; }
    public bool Sensitive { get; }
}
