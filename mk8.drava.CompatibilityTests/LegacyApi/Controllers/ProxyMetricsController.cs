using Microsoft.AspNetCore.Mvc;
using BusinessProxyMetricsAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.Metrics.ProxyMetricsAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/metrics")]
public sealed class ProxyMetricsController : ControllerBase
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
