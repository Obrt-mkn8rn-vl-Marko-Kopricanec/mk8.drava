using BusinessConfigLintFinding = Mk8.Drava.Application.BLL.ControlPlane.ConfigLint.ConfigLintFinding;
using BusinessConfigLintResult = Mk8.Drava.Application.BLL.ControlPlane.ConfigLint.ConfigLintResult;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ConfigLintResponseMapper
{
    public static ConfigLintResponse FromResult(BusinessConfigLintResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            BusinessConfigLintResult.AcceptedResult accepted => FromResult(accepted, succeeded: true),
            BusinessConfigLintResult.RejectedResult rejected => FromResult(rejected, succeeded: false),
            _ => throw new InvalidOperationException($"Unknown config lint result '{result.GetType().Name}'.")};
    }

    private static ConfigLintResponse FromResult(BusinessConfigLintResult result, bool succeeded)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ConfigLintResponse(succeeded: succeeded, lintedAtUtc: result.LintedAtUtc, summary: ConfigLintSummaryResponseMapper.FromSummary(result.Summary), findings: ConfigLintFindingResponseMapper.FromFindings(result.Findings), validationErrors: ProxyConfigurationFileErrorResponseMapper.FromErrors(result.ValidationErrors));
    }
}
