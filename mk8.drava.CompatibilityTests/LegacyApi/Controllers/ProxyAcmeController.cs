using Microsoft.AspNetCore.Mvc;
using BusinessProxyAcmeAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.Acme.ProxyAcmeAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/acme")]
#pragma warning disable CA1515 // ASP.NET Core MVC discovery requires public controller types on this test-only reference ingress.
public sealed class ProxyAcmeController : ControllerBase
#pragma warning restore CA1515
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
