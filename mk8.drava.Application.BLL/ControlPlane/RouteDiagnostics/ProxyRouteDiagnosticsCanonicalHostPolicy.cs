namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record ProxyRouteDiagnosticsCanonicalHostPolicy
{
    public ProxyRouteDiagnosticsCanonicalHostPolicy(bool Enabled, string TargetHost, int StatusCode)
    {
        ArgumentNullException.ThrowIfNull(TargetHost);
        this.Enabled = Enabled;
        this.TargetHost = TargetHost;
        this.StatusCode = StatusCode;
    }

    public bool Enabled { get; }
    public string TargetHost { get; }
    public int StatusCode { get; }
}
