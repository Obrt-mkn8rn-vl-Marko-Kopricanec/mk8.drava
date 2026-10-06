namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record RouteMatchDryRunFinding
{
    public RouteMatchDryRunFinding(string Severity, string Code, string Message)
    {
        ArgumentNullException.ThrowIfNull(Severity);
        ArgumentNullException.ThrowIfNull(Code);
        ArgumentNullException.ThrowIfNull(Message);
        this.Severity = Severity;
        this.Code = Code;
        this.Message = Message;
    }

    public string Severity { get; }
    public string Code { get; }
    public string Message { get; }
}
