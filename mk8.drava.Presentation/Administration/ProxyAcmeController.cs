using Microsoft.AspNetCore.Mvc;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Administration;

// MDRAVA controller routes retained; business operations execute in Application.
[ApiController]
[Route("admin/proxy/acme")]
public sealed class ProxyAcmeController(GatewayAdministrationClient client) : ControllerBase
{
    [HttpGet("status")]
    public Task<IActionResult> StatusAsync()
        => client.ExecuteAsync(HttpContext, ControlOperation.AcmeQuery);
}
