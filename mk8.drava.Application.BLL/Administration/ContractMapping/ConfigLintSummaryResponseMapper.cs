using BusinessConfigLintSummary = Mk8.Drava.Application.BLL.ControlPlane.ConfigLint.ConfigLintSummary;
using BusinessConfigLintStatus = Mk8.Drava.Application.BLL.ControlPlane.ConfigLint.ConfigLintStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ConfigLintSummaryResponseMapper
{
    public static ConfigLintSummaryResponse FromSummary(BusinessConfigLintSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new ConfigLintSummaryResponse(summary.Info, summary.Warning, summary.Error);
    }
}
