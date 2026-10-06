using Microsoft.AspNetCore.Mvc;
using BusinessProxyConfigurationAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationAdministrationService;
using BusinessProxyConfigurationReadAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationReadAdministrationService<Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationProjection>;
using BusinessProxyConfigurationReadResult = Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationReadResult<Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationProjection>;
using BusinessProxyConfigurationReloadAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationReloadAdministrationService<Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationProjection>;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/config")]
public sealed class ProxyConfigurationController : ControllerBase
{
    private readonly BusinessProxyConfigurationAdministrationService _configurationAdministration;
    private readonly BusinessProxyConfigurationReadAdministrationService _configurationReads;
    private readonly BusinessProxyConfigurationReloadAdministrationService _configurationReloads;
    public ProxyConfigurationController(BusinessProxyConfigurationAdministrationService configurationAdministration, BusinessProxyConfigurationReadAdministrationService configurationReads, BusinessProxyConfigurationReloadAdministrationService configurationReloads)
    {
        _configurationAdministration = configurationAdministration;
        _configurationReads = configurationReads;
        _configurationReloads = configurationReloads;
    }

    [HttpPost("normalize")]
    public ActionResult<ProxyConfigurationNormalizeResponse> Normalize([FromBody] ProxyConfigurationNormalizeSubmissionRequest? request)
    {
        var result = _configurationAdministration.Normalize(request?.ToNormalizeRequest());
        var response = ProxyConfigurationNormalizeResponseMapper.FromResult(result);
        return ProxyAdminHttpResultMapper.OkOrBadRequest(this, response, response.Succeeded);
    }

    [HttpPost("reload")]
    public async ValueTask<ActionResult<ProxyConfigurationReloadResponse>> Reload(CancellationToken cancellationToken)
    {
        var result = await _configurationReloads.ReloadAsync(cancellationToken).ConfigureAwait(false);
        var response = ProxyConfigurationReloadResponseMapper.FromResult(result);
        return ProxyAdminHttpResultMapper.OkOrBadRequest(this, response, response.Succeeded);
    }

    [HttpPost("validate")]
    public async ValueTask<ActionResult<ProxyConfigurationValidationResponse>> Validate(CancellationToken cancellationToken)
    {
        var result = await _configurationAdministration.ValidateAsync(cancellationToken).ConfigureAwait(false);
        var response = ProxyConfigurationValidationResponseMapper.FromResult(result);
        return ProxyAdminHttpResultMapper.OkOrBadRequest(this, response, response.Succeeded);
    }

    [HttpGet("active")]
    public ActionResult<ProxyConfigurationResponse> Active()
    {
        var result = _configurationReads.ReadActive();
        return result is BusinessProxyConfigurationReadResult.AvailableResult available ? Ok(ProxyConfigurationResponseMapper.FromProjection(available.Configuration)) : NotFound();
    }

    [HttpGet("effective")]
    public ActionResult<ProxyConfigurationResponse> Effective()
    {
        var result = _configurationReads.ReadEffective();
        return result is BusinessProxyConfigurationReadResult.AvailableResult available ? Ok(ProxyConfigurationResponseMapper.FromProjection(available.Configuration)) : NotFound();
    }
}
