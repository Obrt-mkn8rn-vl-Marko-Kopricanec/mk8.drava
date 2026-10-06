using Microsoft.AspNetCore.Mvc;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Administration;

// MDRAVA controller routes retained; business operations execute in Application.
[ApiController]
[Route("admin/proxy/diagnostics")]
public sealed class ProxyDiagnosticsController(GatewayAdministrationClient client) : ControllerBase
{
    [HttpGet("recent")]
    public Task<IActionResult> RecentAsync([FromQuery] int limit = 50)
        => client.ExecuteAsync(HttpContext, ControlOperation.DiagnosticsQuery, new RecentItemsRequest(limit));
}
