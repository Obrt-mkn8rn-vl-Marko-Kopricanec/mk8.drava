using Microsoft.AspNetCore.Mvc;
using BusinessProxyBackupAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/backup")]
#pragma warning disable CA1515 // ASP.NET Core MVC discovery requires public controller types on this test-only reference ingress.
public sealed class ProxyBackupController : ControllerBase
#pragma warning restore CA1515
{
    private readonly BusinessProxyBackupAdministrationService _backupAdministration;
    public ProxyBackupController(BusinessProxyBackupAdministrationService backupAdministration)
    {
        _backupAdministration = backupAdministration;
    }

    [HttpGet("manifest")]
    public ProxyBackupManifestResponse Manifest()
    {
        var manifest = _backupAdministration.CreateManifest();
        return ProxyBackupManifestResponseMapper.FromManifest(manifest);
    }

    [HttpPost("validate")]
    public async ValueTask<ActionResult<ProxyRestoreValidationResponseBody>> ValidateAsync(CancellationToken cancellationToken)
    {
        var result = await _backupAdministration.ValidateAsync(cancellationToken).ConfigureAwait(false);
        var response = ProxyRestoreValidationResponseBodyMapper.FromResult(result);
        return ProxyAdminHttpResultMapper.OkOrBadRequest(this, response, response.Succeeded);
    }
}
