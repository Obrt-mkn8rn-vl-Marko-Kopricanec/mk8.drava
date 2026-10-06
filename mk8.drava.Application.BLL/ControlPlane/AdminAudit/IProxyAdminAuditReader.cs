namespace Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
public interface IProxyAdminAuditReader
{
    IReadOnlyList<ProxyAdminAuditEvent> Recent(int limit);
}
