using Microsoft.AspNetCore.Mvc;
using BusinessProxyCacheAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.Caching.ProxyCacheAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/cache")]
#pragma warning disable CA1515 // ASP.NET Core MVC discovery requires public controller types on this test-only reference ingress.
public sealed class ProxyCacheController : ControllerBase
#pragma warning restore CA1515
{
    private readonly BusinessProxyCacheAdministrationService _cacheAdministration;
    public ProxyCacheController(BusinessProxyCacheAdministrationService cacheAdministration)
    {
        _cacheAdministration = cacheAdministration;
    }

    [HttpGet("status")]
    public ProxyCacheStatusResponse Status()
    {
        var status = _cacheAdministration.GetStatus();
        return ProxyCacheStatusResponseMapper.FromStatus(status);
    }

    [HttpPost("clear")]
    public ProxyCacheStatusResponse Clear()
    {
        var status = _cacheAdministration.Clear();
        return ProxyCacheStatusResponseMapper.FromStatus(status);
    }
}
