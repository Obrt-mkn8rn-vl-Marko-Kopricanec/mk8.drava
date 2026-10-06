using BusinessProxyAdminAuditEvent = Mk8.Drava.Application.BLL.ControlPlane.AdminAudit.ProxyAdminAuditEvent;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyAdminAuditEventResponseMapper
{
    public static IReadOnlyList<ProxyAdminAuditEventResponse> FromEvents(IReadOnlyList<BusinessProxyAdminAuditEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return ApiResponseList.Copy(events.Select(FromEvent));
    }

    private static ProxyAdminAuditEventResponse FromEvent(BusinessProxyAdminAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        return new ProxyAdminAuditEventResponse(auditEvent.TimestampUtc, auditEvent.Method, auditEvent.Path, auditEvent.ClientIp, auditEvent.AuthResult, auditEvent.StatusCode, auditEvent.Succeeded);
    }
}
