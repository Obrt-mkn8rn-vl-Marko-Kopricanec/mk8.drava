using Microsoft.AspNetCore.Mvc;
using BusinessProxyAcmeAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.Acme.ProxyAcmeAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/acme")]
public sealed class ProxyAcmeController : ControllerBase
{
    private readonly BusinessProxyAcmeAdministrationService _acmeAdministration;
    public ProxyAcmeController(BusinessProxyAcmeAdministrationService acmeAdministration)
    {
        _acmeAdministration = acmeAdministration;
    }

    [HttpGet("status")]
    public ActionResult<AcmeStatusResponse> Status()
    {
        var status = _acmeAdministration.GetStatus();
        return ProxyAdminHttpResultMapper.OkOrNotFound(this, status is null ? null : AcmeStatusResponseMapper.FromStatus(status));
    }
}
