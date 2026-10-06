using Microsoft.AspNetCore.Mvc;
using BusinessProxyDiagnosticsAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics.ProxyDiagnosticsAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/diagnostics")]
public sealed class ProxyDiagnosticsController : ControllerBase
{
    private readonly BusinessProxyDiagnosticsAdministrationService _diagnosticsAdministration;
    public ProxyDiagnosticsController(BusinessProxyDiagnosticsAdministrationService diagnosticsAdministration)
    {
        _diagnosticsAdministration = diagnosticsAdministration;
    }

    [HttpGet("recent")]
    public IReadOnlyList<ProxyRecentRequestDiagnosticEventResponse> Recent([FromQuery] int limit = 50)
    {
        return ProxyRecentRequestDiagnosticEventResponseMapper.FromEvents(_diagnosticsAdministration.Recent(limit));
    }
}
