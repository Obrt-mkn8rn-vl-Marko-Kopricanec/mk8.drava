namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record ProxyRouteDiagnosticsRedirectPolicy
{
    public ProxyRouteDiagnosticsRedirectPolicy(int StatusCode, string TargetUrl, string TargetPath, bool PreserveQuery)
    {
        ArgumentNullException.ThrowIfNull(TargetUrl);
        ArgumentNullException.ThrowIfNull(TargetPath);
        this.StatusCode = StatusCode;
        this.TargetUrl = TargetUrl;
        this.TargetPath = TargetPath;
        this.PreserveQuery = PreserveQuery;
    }

    public int StatusCode { get; }
    public string TargetUrl { get; }
    public string TargetPath { get; }
    public bool PreserveQuery { get; }
}
