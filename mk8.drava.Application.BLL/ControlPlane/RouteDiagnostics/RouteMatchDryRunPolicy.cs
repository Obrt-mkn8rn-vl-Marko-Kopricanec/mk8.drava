namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record RouteMatchDryRunPolicy
{
    public RouteMatchDryRunPolicy(bool Enabled, bool WouldApply, string Reason)
    {
        ArgumentNullException.ThrowIfNull(Reason);
        this.Enabled = Enabled;
        this.WouldApply = WouldApply;
        this.Reason = Reason;
    }

    public bool Enabled { get; }
    public bool WouldApply { get; }
    public string Reason { get; }

    public static RouteMatchDryRunPolicy Disabled(string reason)
    {
        return new RouteMatchDryRunPolicy(Enabled: false, WouldApply: false, reason);
    }

    public static RouteMatchDryRunPolicy EnabledButBlocked(string reason)
    {
        return new RouteMatchDryRunPolicy(Enabled: true, WouldApply: false, reason);
    }

    public static RouteMatchDryRunPolicy EnabledAndApplies(string reason)
    {
        return new RouteMatchDryRunPolicy(Enabled: true, WouldApply: true, reason);
    }
}
