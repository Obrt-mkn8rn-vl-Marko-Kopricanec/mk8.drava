namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed record ProxyBackupDirectoryRequirement
{
    public ProxyBackupDirectoryRequirement(string RelativePath, string Classification, bool Sensitive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RelativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(Classification);
        this.RelativePath = RelativePath;
        this.Classification = Classification;
        this.Sensitive = Sensitive;
    }

    public string RelativePath { get; }
    public string Classification { get; }
    public bool Sensitive { get; }
}
