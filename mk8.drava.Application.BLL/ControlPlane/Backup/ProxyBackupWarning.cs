namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed record ProxyBackupWarning
{
    public ProxyBackupWarning(string Code, string Message, string? RelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(Message);
        if (RelativePath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(RelativePath);
        }

        this.Code = Code;
        this.Message = Message;
        this.RelativePath = RelativePath;
    }

    public string Code { get; }
    public string Message { get; }
    public string? RelativePath { get; }
}
