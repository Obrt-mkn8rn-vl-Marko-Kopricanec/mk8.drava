using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyLogSummarySourceMapper
{
    public static ProxyLogSummarySource FromStatus(ProxyLogPersistenceStatus logPersistence)
    {
        ArgumentNullException.ThrowIfNull(logPersistence);
        return new ProxyLogSummarySource(logPersistence.AccessLogEnabled, logPersistence.AdminAuditEnabled, logPersistence.State, logPersistence.Reason);
    }
}
