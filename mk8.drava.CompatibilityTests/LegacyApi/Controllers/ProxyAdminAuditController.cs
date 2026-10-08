using Microsoft.AspNetCore.Mvc;
using BusinessProxyAdminAuditAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.AdminAudit.ProxyAdminAuditAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/audit")]
#pragma warning disable CA1515 // ASP.NET Core MVC discovery requires public controller types on this test-only reference ingress.
public sealed class ProxyAdminAuditController : ControllerBase
#pragma warning restore CA1515
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
