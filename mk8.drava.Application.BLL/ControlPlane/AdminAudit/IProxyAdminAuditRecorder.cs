namespace Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
public interface IProxyAdminAuditRecorder
{
    void Add(ProxyAdminAuditEvent auditEvent, int capacity);
}
