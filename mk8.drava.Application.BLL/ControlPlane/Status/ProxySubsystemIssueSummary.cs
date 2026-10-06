namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxySubsystemIssueSummary
{
    public ProxySubsystemIssueSummary(DateTimeOffset TimestampUtc, string Category, string Reason, string? AffectedIdentity)
    {
        ProxyStatusFacts.RequireText(Category, nameof(Category));
        ProxyStatusFacts.RequireText(Reason, nameof(Reason));
        ProxyStatusFacts.RequireOptionalText(AffectedIdentity, nameof(AffectedIdentity));
        this.TimestampUtc = TimestampUtc;
        this.Category = Category;
        this.Reason = Reason;
        this.AffectedIdentity = AffectedIdentity;
    }

    public DateTimeOffset TimestampUtc { get; }
    public string Category { get; }
    public string Reason { get; }
    public string? AffectedIdentity { get; }
}
