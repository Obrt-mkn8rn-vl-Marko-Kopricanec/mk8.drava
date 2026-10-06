namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed record ProxyBackupFileClassification
{
    public ProxyBackupFileClassification(string Category, string Classification, bool Sensitive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(Classification);
        this.Category = Category;
        this.Classification = Classification;
        this.Sensitive = Sensitive;
    }

    public string Category { get; }
    public string Classification { get; }
    public bool Sensitive { get; }
}
