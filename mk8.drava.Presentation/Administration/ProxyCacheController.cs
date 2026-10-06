using Microsoft.AspNetCore.Mvc;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Administration;

// MDRAVA controller routes retained; business operations execute in Application.
[ApiController]
[Route("admin/proxy/cache")]
public sealed class ProxyCacheController(GatewayAdministrationClient client) : ControllerBase
{
    [HttpGet("status")]
    public Task<IActionResult> StatusAsync()
        => client.ExecuteAsync(HttpContext, ControlOperation.CacheQuery);

    [HttpPost("clear")]
    public Task<IActionResult> ClearAsync()
        => client.ExecuteAsync(HttpContext, ControlOperation.CacheClear);
}
