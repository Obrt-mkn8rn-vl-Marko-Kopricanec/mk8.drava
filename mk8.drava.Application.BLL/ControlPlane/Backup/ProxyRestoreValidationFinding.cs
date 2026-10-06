namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed record ProxyRestoreValidationFinding
{
    public ProxyRestoreValidationFinding(string Severity, string Code, string Message, string? RelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Severity);
        ArgumentException.ThrowIfNullOrWhiteSpace(Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(Message);
        if (RelativePath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(RelativePath);
        }

        this.Severity = Severity;
        this.Code = Code;
        this.Message = Message;
        this.RelativePath = RelativePath;
    }

    public string Severity { get; }
    public string Code { get; }
    public string Message { get; }
    public string? RelativePath { get; }
}
