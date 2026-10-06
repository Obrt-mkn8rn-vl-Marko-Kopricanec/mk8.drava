namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed record ProxyBackupFileSystemWarning
{
    public ProxyBackupFileSystemWarning(string Code, string? RelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Code);
        if (RelativePath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(RelativePath);
        }

        this.Code = Code;
        this.RelativePath = RelativePath;
    }

    public string Code { get; }
    public string? RelativePath { get; }
}
