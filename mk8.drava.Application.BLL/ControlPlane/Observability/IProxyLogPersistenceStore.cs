using Mk8.Drava.Application.BLL.ControlPlane.Status;
using Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;

namespace Mk8.Drava.Application.BLL.ControlPlane.Observability;
public interface IProxyLogPersistenceStore
{
    void WriteAccess(ProxyAccessLogEntry entry);
    void WriteAdminAudit(ProxyAdminAuditEvent auditEvent);
    ProxyLogPersistenceStatus GetStatus();
}
