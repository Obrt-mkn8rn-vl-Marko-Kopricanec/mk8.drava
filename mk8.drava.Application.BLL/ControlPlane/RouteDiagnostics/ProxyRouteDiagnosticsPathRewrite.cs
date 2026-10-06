namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record ProxyRouteDiagnosticsPathRewrite
{
    public ProxyRouteDiagnosticsPathRewrite(string StripPrefix, string ReplacePrefix, string Replacement)
    {
        ArgumentNullException.ThrowIfNull(StripPrefix);
        ArgumentNullException.ThrowIfNull(ReplacePrefix);
        ArgumentNullException.ThrowIfNull(Replacement);
        this.StripPrefix = StripPrefix;
        this.ReplacePrefix = ReplacePrefix;
        this.Replacement = Replacement;
    }

    public string StripPrefix { get; }
    public string ReplacePrefix { get; }
    public string Replacement { get; }
}
