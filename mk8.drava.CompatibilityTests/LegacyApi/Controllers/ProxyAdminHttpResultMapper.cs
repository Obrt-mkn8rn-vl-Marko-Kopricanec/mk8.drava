using System.Text;
using Microsoft.AspNetCore.Mvc;
using BusinessProxyMetricsExportResult = Mk8.Drava.Application.BLL.ControlPlane.Metrics.ProxyMetricsExportResult;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
internal static class ProxyAdminHttpResultMapper
{
    public static ActionResult<T> OkOrBadRequest<T>(ControllerBase controller, T result, bool succeeded)
    {
        return succeeded ? controller.Ok(result) : controller.BadRequest(result);
    }

    public static ActionResult<T> OkOrNotFound<T>(ControllerBase controller, T? result)
        where T : class
    {
        return result is null ? controller.NotFound() : controller.Ok(result);
    }

    public static ActionResult<T> OkOrNotFound<T>(ControllerBase controller, bool found, T? result)
        where T : class
    {
        return found && result is not null ? controller.Ok(result) : controller.NotFound();
    }

    public static IActionResult TextExportOrNotFound(ControllerBase controller, BusinessProxyMetricsExportResult result)
    {
        return result is BusinessProxyMetricsExportResult.ExportedResult exported ? controller.Content(exported.Content, exported.ContentType, Encoding.UTF8) : controller.NotFound();
    }
}
