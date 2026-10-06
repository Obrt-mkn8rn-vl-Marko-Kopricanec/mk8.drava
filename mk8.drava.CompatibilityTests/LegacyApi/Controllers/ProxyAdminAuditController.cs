using Microsoft.AspNetCore.Mvc;
using BusinessProxyAdminAuditAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.AdminAudit.ProxyAdminAuditAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/audit")]
public sealed class ProxyAdminAuditController : ControllerBase
{
    private readonly BusinessProxyAdminAuditAdministrationService _auditAdministration;
    public ProxyAdminAuditController(BusinessProxyAdminAuditAdministrationService auditAdministration)
    {
        _auditAdministration = auditAdministration;
    }

    [HttpGet("recent")]
    public IReadOnlyList<ProxyAdminAuditEventResponse> Recent([FromQuery] int limit = 50)
    {
        return ProxyAdminAuditEventResponseMapper.FromEvents(_auditAdministration.Recent(limit));
    }
}
