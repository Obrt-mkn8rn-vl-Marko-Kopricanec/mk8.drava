using BusinessProxyRestoreValidationFinding = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyRestoreValidationFinding;
using BusinessProxyRestoreValidationResult = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyRestoreValidationResult;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyRestoreValidationFindingResponseMapper
{
    public static IReadOnlyList<ProxyRestoreValidationFindingResponse> FromFindings(IReadOnlyList<BusinessProxyRestoreValidationFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return ApiResponseList.Copy(findings.Select(FromFinding));
    }

    private static ProxyRestoreValidationFindingResponse FromFinding(BusinessProxyRestoreValidationFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return new ProxyRestoreValidationFindingResponse(finding.Severity, finding.Code, finding.Message, finding.RelativePath);
    }
}
