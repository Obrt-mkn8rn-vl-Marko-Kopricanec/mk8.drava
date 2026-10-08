using Microsoft.AspNetCore.Mvc;
using BusinessProxyRouteDiagnosticsAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.ProxyRouteDiagnosticsAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/routes")]
#pragma warning disable CA1515 // ASP.NET Core MVC discovery requires public controller types on this test-only reference ingress.
public sealed class ProxyRouteDiagnosticsController : ControllerBase
#pragma warning restore CA1515
{
    private readonly BusinessProxyRouteDiagnosticsAdministrationService _routeDiagnosticsAdministration;
    public ProxyRouteDiagnosticsController(BusinessProxyRouteDiagnosticsAdministrationService routeDiagnosticsAdministration)
    {
        _routeDiagnosticsAdministration = routeDiagnosticsAdministration;
    }

    [HttpPost("match")]
    public ActionResult<RouteMatchDryRunResponse> Match([FromBody] ProxyRouteMatchDryRunRequest? request)
    {
        var result = _routeDiagnosticsAdministration.Match(request?.ToRouteMatchDryRunRequest());
        var response = RouteMatchDryRunResponseMapper.FromResult(result);
        return ProxyAdminHttpResultMapper.OkOrBadRequest(this, response, response.Succeeded);
    }
}
