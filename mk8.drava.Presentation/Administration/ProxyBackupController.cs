using Microsoft.AspNetCore.Mvc;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Administration;

// MDRAVA controller routes retained; business operations execute in Application.
[ApiController]
[Route("admin/proxy/backup")]
public sealed class ProxyBackupController(GatewayAdministrationClient client) : ControllerBase
{
    [HttpGet("manifest")]
    public Task<IActionResult> ManifestAsync()
        => client.ExecuteAsync(HttpContext, ControlOperation.BackupQuery);

    [HttpPost("validate")]
    public Task<IActionResult> ValidateAsync()
        => client.ExecuteAsync(HttpContext, ControlOperation.RestoreValidate);
}
