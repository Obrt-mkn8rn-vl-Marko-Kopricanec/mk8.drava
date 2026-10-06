namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyLogSubsystemSummary
{
    public ProxyLogSubsystemSummary(bool AccessLogPersistenceEnabled, bool AdminAuditPersistenceEnabled, string State, string Reason)
    {
        ProxyStatusFacts.RequireText(State, nameof(State));
        ProxyStatusFacts.RequireText(Reason, nameof(Reason));
        this.AccessLogPersistenceEnabled = AccessLogPersistenceEnabled;
        this.AdminAuditPersistenceEnabled = AdminAuditPersistenceEnabled;
        this.State = State;
        this.Reason = Reason;
    }

    public bool AccessLogPersistenceEnabled { get; }
    public bool AdminAuditPersistenceEnabled { get; }
    public string State { get; }
    public string Reason { get; }
    public static ProxyLogSubsystemSummary Unknown { get; } = new(false, false, ProxyStatusText.Unknown, ProxyStatusText.NotAvailable);
}
