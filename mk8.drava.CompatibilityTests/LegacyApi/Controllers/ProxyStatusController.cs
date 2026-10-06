using Microsoft.AspNetCore.Mvc;
using BusinessProxyStatusAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyStatusAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy")]
public sealed class ProxyStatusController : ControllerBase
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
