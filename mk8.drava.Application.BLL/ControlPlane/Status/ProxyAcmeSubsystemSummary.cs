namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyAcmeSubsystemSummary
{
    public ProxyAcmeSubsystemSummary(bool Enabled, int Configured, int Active, int Failed, int RenewalBackoff, ProxySubsystemIssueSummary? LastIssue)
    {
        ProxyStatusFacts.RequireNonNegative(Configured, nameof(Configured));
        ProxyStatusFacts.RequireNonNegative(Active, nameof(Active));
        ProxyStatusFacts.RequireNonNegative(Failed, nameof(Failed));
        ProxyStatusFacts.RequireNonNegative(RenewalBackoff, nameof(RenewalBackoff));
        this.Enabled = Enabled;
        this.Configured = Configured;
        this.Active = Active;
        this.Failed = Failed;
        this.RenewalBackoff = RenewalBackoff;
        this.LastIssue = LastIssue;
    }

    public bool Enabled { get; }
    public int Configured { get; }
    public int Active { get; }
    public int Failed { get; }
    public int RenewalBackoff { get; }
    public ProxySubsystemIssueSummary? LastIssue { get; }
    public static ProxyAcmeSubsystemSummary Unknown { get; } = new(false, 0, 0, 0, 0, null);
}
