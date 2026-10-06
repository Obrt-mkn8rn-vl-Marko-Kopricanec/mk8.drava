namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record ProxyRouteDiagnosticsMaintenancePolicy
{
    public ProxyRouteDiagnosticsMaintenancePolicy(bool Enabled, int? RetryAfterSeconds, string ContentType, string Body)
    {
        ArgumentNullException.ThrowIfNull(ContentType);
        ArgumentNullException.ThrowIfNull(Body);
        this.Enabled = Enabled;
        this.RetryAfterSeconds = RetryAfterSeconds;
        this.ContentType = ContentType;
        this.Body = Body;
    }

    public bool Enabled { get; }
    public int? RetryAfterSeconds { get; }
    public string ContentType { get; }
    public string Body { get; }
}
