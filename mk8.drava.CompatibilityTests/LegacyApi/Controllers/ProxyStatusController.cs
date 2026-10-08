using Microsoft.AspNetCore.Mvc;
using BusinessProxyStatusAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyStatusAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy")]
#pragma warning disable CA1515 // ASP.NET Core MVC discovery requires public controller types on this test-only reference ingress.
public sealed class ProxyStatusController : ControllerBase
#pragma warning restore CA1515
{
    private readonly BusinessProxyStatusAdministrationService _statusAdministration;
    public ProxyStatusController(BusinessProxyStatusAdministrationService statusAdministration)
    {
        _statusAdministration = statusAdministration;
    }

    [HttpGet("status")]
    public ProxyStatusResponse Get()
    {
        var status = _statusAdministration.GetStatus();
        return ProxyStatusResponseMapper.FromBusinessResponse(status);
    }
}
