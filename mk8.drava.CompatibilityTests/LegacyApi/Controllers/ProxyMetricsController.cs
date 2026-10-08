using Microsoft.AspNetCore.Mvc;
using BusinessProxyMetricsAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.Metrics.ProxyMetricsAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/metrics")]
#pragma warning disable CA1515 // ASP.NET Core MVC discovery requires public controller types on this test-only reference ingress.
public sealed class ProxyMetricsController : ControllerBase
#pragma warning restore CA1515
{
    private readonly BusinessProxyMetricsAdministrationService _metricsAdministration;
    public ProxyMetricsController(BusinessProxyMetricsAdministrationService metricsAdministration)
    {
        _metricsAdministration = metricsAdministration;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return ProxyAdminHttpResultMapper.TextExportOrNotFound(this, _metricsAdministration.Export());
    }
}
