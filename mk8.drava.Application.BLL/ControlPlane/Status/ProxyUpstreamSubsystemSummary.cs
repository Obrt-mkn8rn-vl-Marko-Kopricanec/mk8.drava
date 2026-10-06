namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyUpstreamSubsystemSummary
{
    public ProxyUpstreamSubsystemSummary(int Total, int Healthy, int Unhealthy, int UnknownHealth, int HealthChecksEnabled)
    {
        ProxyStatusFacts.RequireNonNegative(Total, nameof(Total));
        ProxyStatusFacts.RequireNonNegative(Healthy, nameof(Healthy));
        ProxyStatusFacts.RequireNonNegative(Unhealthy, nameof(Unhealthy));
        ProxyStatusFacts.RequireNonNegative(UnknownHealth, nameof(UnknownHealth));
        ProxyStatusFacts.RequireNonNegative(HealthChecksEnabled, nameof(HealthChecksEnabled));
        this.Total = Total;
        this.Healthy = Healthy;
        this.Unhealthy = Unhealthy;
        this.UnknownHealth = UnknownHealth;
        this.HealthChecksEnabled = HealthChecksEnabled;
    }

    public int Total { get; }
    public int Healthy { get; }
    public int Unhealthy { get; }
    public int UnknownHealth { get; }
    public int HealthChecksEnabled { get; }
    public static ProxyUpstreamSubsystemSummary Unknown { get; } = new(0, 0, 0, 0, 0);
}
