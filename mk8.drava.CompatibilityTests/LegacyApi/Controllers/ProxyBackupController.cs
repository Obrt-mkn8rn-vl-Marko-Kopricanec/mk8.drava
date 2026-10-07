using Microsoft.AspNetCore.Mvc;
using BusinessProxyBackupAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/backup")]
public sealed class ProxyBackupController : ControllerBase
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
