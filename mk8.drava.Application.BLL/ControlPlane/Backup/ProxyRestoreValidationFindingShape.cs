using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public sealed record ProxyRestoreValidationFindingShape
{
    public ProxyRestoreValidationFindingShape(string Code, string Message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(Message);
        this.Code = Code;
        this.Message = Message;
    }

    public string Code { get; }
    public string Message { get; }
}
