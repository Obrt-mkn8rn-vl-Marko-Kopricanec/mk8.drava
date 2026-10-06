using Microsoft.AspNetCore.Mvc;
using BusinessProxyRouteDiagnosticsAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.ProxyRouteDiagnosticsAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/routes")]
public sealed class ProxyRouteDiagnosticsController : ControllerBase
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
