using Microsoft.AspNetCore.Mvc;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Administration;

// MDRAVA controller routes retained; business operations execute in Application.
[ApiController]
[Route("admin/proxy/config")]
public sealed class ProxyConfigurationController(GatewayAdministrationClient client) : ControllerBase
{
    [HttpPost("normalize")]
    public Task<IActionResult> NormalizeAsync()
        => client.ExecuteAsync(HttpContext, ControlOperation.ConfigurationNormalize);

    [HttpPost("reload")]
    public Task<IActionResult> ReloadAsync()
        => client.ExecuteAsync(HttpContext, ControlOperation.ConfigurationReload);

    [HttpPost("validate")]
    public Task<IActionResult> ValidateAsync()
        => client.ExecuteAsync(HttpContext, ControlOperation.ConfigurationValidate);

    [HttpGet("active")]
    public Task<IActionResult> ActiveAsync()
        => client.ExecuteAsync(HttpContext, ControlOperation.ConfigurationQuery, new ConfigurationQueryRequest());

    [HttpGet("effective")]
    public Task<IActionResult> EffectiveAsync()
        => client.ExecuteAsync(HttpContext, ControlOperation.ConfigurationQuery, new ConfigurationQueryRequest(Effective: true));
}
