using BusinessConfigLintFinding = Mk8.Drava.Application.BLL.ControlPlane.ConfigLint.ConfigLintFinding;
using BusinessConfigLintResult = Mk8.Drava.Application.BLL.ControlPlane.ConfigLint.ConfigLintResult;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ConfigLintFindingResponseMapper
{
    public static IReadOnlyList<ConfigLintFindingResponse> FromFindings(IReadOnlyList<BusinessConfigLintFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return ApiResponseList.Copy(findings.Select(FromFinding));
    }

    private static ConfigLintFindingResponse FromFinding(BusinessConfigLintFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return new ConfigLintFindingResponse(finding.Severity, finding.Code, finding.Message, finding.Source, finding.Path, finding.SuggestedFix);
    }
}
