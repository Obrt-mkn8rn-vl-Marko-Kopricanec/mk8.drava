namespace Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
public sealed class ProxyAdminAuditAdministrationService
{
    private readonly IProxyAdminAuditReader _reader;
    public ProxyAdminAuditAdministrationService(IProxyAdminAuditReader reader)
    {
        _reader = reader;
    }

    public IReadOnlyList<ProxyAdminAuditEvent> Recent(int limit)
    {
        return _reader.Recent(limit);
    }
}
