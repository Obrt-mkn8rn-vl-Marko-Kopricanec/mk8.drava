namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyCertificateSubsystemSummary
{
    public ProxyCertificateSubsystemSummary(int Configured, int Loaded, int MissingReferences, int Expired, int NotYetValid, int ExpiringSoon, ProxySubsystemIssueSummary? LastIssue)
    {
        ProxyStatusFacts.RequireNonNegative(Configured, nameof(Configured));
        ProxyStatusFacts.RequireNonNegative(Loaded, nameof(Loaded));
        ProxyStatusFacts.RequireNonNegative(MissingReferences, nameof(MissingReferences));
        ProxyStatusFacts.RequireNonNegative(Expired, nameof(Expired));
        ProxyStatusFacts.RequireNonNegative(NotYetValid, nameof(NotYetValid));
        ProxyStatusFacts.RequireNonNegative(ExpiringSoon, nameof(ExpiringSoon));
        this.Configured = Configured;
        this.Loaded = Loaded;
        this.MissingReferences = MissingReferences;
        this.Expired = Expired;
        this.NotYetValid = NotYetValid;
        this.ExpiringSoon = ExpiringSoon;
        this.LastIssue = LastIssue;
    }

    public int Configured { get; }
    public int Loaded { get; }
    public int MissingReferences { get; }
    public int Expired { get; }
    public int NotYetValid { get; }
    public int ExpiringSoon { get; }
    public ProxySubsystemIssueSummary? LastIssue { get; }
    public static ProxyCertificateSubsystemSummary Unknown { get; } = new(0, 0, 0, 0, 0, 0, null);
}
