using Microsoft.AspNetCore.Mvc;
using BusinessProxyDiagnosticsAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics.ProxyDiagnosticsAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/diagnostics")]
#pragma warning disable CA1515 // ASP.NET Core MVC discovery requires public controller types on this test-only reference ingress.
public sealed class ProxyDiagnosticsController : ControllerBase
#pragma warning restore CA1515
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
