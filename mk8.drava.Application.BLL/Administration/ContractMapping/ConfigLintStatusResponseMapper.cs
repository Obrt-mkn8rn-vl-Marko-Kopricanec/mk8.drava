using BusinessConfigLintSummary = Mk8.Drava.Application.BLL.ControlPlane.ConfigLint.ConfigLintSummary;
using BusinessConfigLintStatus = Mk8.Drava.Application.BLL.ControlPlane.ConfigLint.ConfigLintStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ConfigLintStatusResponseMapper
{
    public static ConfigLintStatusResponse FromStatus(BusinessConfigLintStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ConfigLintStatusResponse(status.Available, status.LastActiveLintAtUtc, status.LastActiveLintSummary is null ? null : ConfigLintSummaryResponseMapper.FromSummary(status.LastActiveLintSummary));
    }
}
