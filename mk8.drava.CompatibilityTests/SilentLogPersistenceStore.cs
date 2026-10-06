using Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
using Mk8.Drava.Application.BLL.ControlPlane.Observability;
using Mk8.Drava.Application.BLL.ControlPlane.Status;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal sealed class SilentLogPersistenceStore : IProxyLogPersistenceStore
{
    public static SilentLogPersistenceStore Instance { get; } = new();

    private SilentLogPersistenceStore()
    {
    }

    public void WriteAccess(ProxyAccessLogEntry entry)
    {
    }

    public void WriteAdminAudit(ProxyAdminAuditEvent auditEvent)
    {
    }

    public ProxyLogPersistenceStatus GetStatus()
    {
        return ProxyLogPersistenceStatus.Unknown;
    }
}
