namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyRuntimePreflightCheck
{
    public ProxyRuntimePreflightCheck(string Name, string RelativePath, bool Exists, bool Created, bool CanRead, bool CanWrite, string Severity, string Reason)
    {
        ProxyStatusFacts.RequireText(Name, nameof(Name));
        ProxyStatusFacts.RequireText(RelativePath, nameof(RelativePath));
        ProxyStatusFacts.RequireText(Severity, nameof(Severity));
        ProxyStatusFacts.RequireText(Reason, nameof(Reason));
        this.Name = Name;
        this.RelativePath = RelativePath;
        this.Exists = Exists;
        this.Created = Created;
        this.CanRead = CanRead;
        this.CanWrite = CanWrite;
        this.Severity = Severity;
        this.Reason = Reason;
    }

    public string Name { get; }
    public string RelativePath { get; }
    public bool Exists { get; }
    public bool Created { get; }
    public bool CanRead { get; }
    public bool CanWrite { get; }
    public string Severity { get; }
    public string Reason { get; }
}
