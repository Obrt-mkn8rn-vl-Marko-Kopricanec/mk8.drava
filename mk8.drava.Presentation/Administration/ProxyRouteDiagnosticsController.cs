using Microsoft.AspNetCore.Mvc;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Administration;

// MDRAVA controller routes retained; business operations execute in Application.
[ApiController]
[Route("admin/proxy/routes")]
public sealed class ProxyRouteDiagnosticsController(GatewayAdministrationClient client) : ControllerBase
{
    [HttpPost("match")]
    public Task<IActionResult> MatchAsync()
        => client.ExecuteAsync(HttpContext, ControlOperation.RouteDryRun);
}
