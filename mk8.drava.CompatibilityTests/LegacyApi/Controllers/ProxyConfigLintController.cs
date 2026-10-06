using Microsoft.AspNetCore.Mvc;
using BusinessProxyConfigLintAdministrationService = Mk8.Drava.Application.BLL.ControlPlane.ConfigLint.ProxyConfigLintAdministrationService;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
[ApiController]
[Route("admin/proxy/config/lint")]
public sealed class ProxyConfigLintController : ControllerBase
{
    private readonly BusinessProxyConfigLintAdministrationService _configLintAdministration;
    public ProxyConfigLintController(BusinessProxyConfigLintAdministrationService configLintAdministration)
    {
        _configLintAdministration = configLintAdministration;
    }

    [HttpGet]
    public ActionResult<ConfigLintResponse> Active()
    {
        var result = _configLintAdministration.LintActive();
        var response = ConfigLintResponseMapper.FromResult(result);
        return ProxyAdminHttpResultMapper.OkOrBadRequest(this, response, response.Succeeded);
    }

    [HttpPost]
    public ActionResult<ConfigLintResponse> Submitted([FromBody] ProxyConfigLintSubmissionRequest? request)
    {
        var result = _configLintAdministration.LintSubmitted(request?.ToConfigLintRequest());
        var response = ConfigLintResponseMapper.FromResult(result);
        return ProxyAdminHttpResultMapper.OkOrBadRequest(this, response, response.Succeeded);
    }
}
