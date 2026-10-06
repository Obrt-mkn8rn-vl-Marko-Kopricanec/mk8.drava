using Microsoft.AspNetCore.Mvc;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Administration;

[ApiController]
[Route("admin/drava")]
public sealed class DravaController(GatewayAdministrationClient client) : ControllerBase
{
    [HttpGet("policy")]
    public Task<IActionResult> PolicyAsync(bool includeHistory = false, string afterServiceId = "", int limit = 100)
        => client.ExecuteAsync(HttpContext, ControlOperation.PolicyQuery, new PolicyQueryRequest { IncludeHistory = includeHistory, AfterServiceId = afterServiceId, Limit = limit });

    [HttpPost("policy")]
    public Task<IActionResult> UpdatePolicyAsync() => client.ExecuteAsync(HttpContext, ControlOperation.PolicyUpdate);

    [HttpPost("policy/rollback")]
    public Task<IActionResult> RollbackPolicyAsync() => client.ExecuteAsync(HttpContext, ControlOperation.PolicyRollback);

    [HttpGet("registry")]
    public Task<IActionResult> RegistryAsync(string kind = "instances", string afterId = "", int limit = 100)
        => client.ExecuteAsync(HttpContext, ControlOperation.RegistryQuery, new RegistryQueryRequest { Kind = kind, AfterId = afterId, Limit = limit });

    [HttpPost("nodes/revoke")]
    public Task<IActionResult> RevokeNodeAsync() => client.ExecuteAsync(HttpContext, ControlOperation.NodeRevoke);

    [HttpPost("instances/revoke")]
    public Task<IActionResult> RevokeInstanceAsync() => client.ExecuteAsync(HttpContext, ControlOperation.InstanceRevoke);
}
