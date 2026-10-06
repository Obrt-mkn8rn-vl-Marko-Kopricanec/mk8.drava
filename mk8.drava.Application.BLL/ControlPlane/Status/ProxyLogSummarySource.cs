using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyLogSummarySource
{
    public ProxyLogSummarySource(bool AccessLogPersistenceEnabled, bool AdminAuditPersistenceEnabled, string State, string Reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(State);
        ArgumentException.ThrowIfNullOrWhiteSpace(Reason);
        this.AccessLogPersistenceEnabled = AccessLogPersistenceEnabled;
        this.AdminAuditPersistenceEnabled = AdminAuditPersistenceEnabled;
        this.State = State;
        this.Reason = Reason;
    }

    public bool AccessLogPersistenceEnabled { get; }
    public bool AdminAuditPersistenceEnabled { get; }
    public string State { get; }
    public string Reason { get; }
}
