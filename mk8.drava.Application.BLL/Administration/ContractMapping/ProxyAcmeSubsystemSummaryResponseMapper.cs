using BusinessProxyAcmeSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyAcmeSubsystemSummary;
using BusinessProxyCertificateSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyCertificateSubsystemSummary;
using BusinessProxySubsystemIssueSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxySubsystemIssueSummary;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyAcmeSubsystemSummaryResponseMapper
{
    public static ProxyAcmeSubsystemSummaryResponse FromSummary(BusinessProxyAcmeSubsystemSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new ProxyAcmeSubsystemSummaryResponse(summary.Enabled, summary.Configured, summary.Active, summary.Failed, summary.RenewalBackoff, summary.LastIssue is null ? null : ProxySubsystemIssueSummaryResponseMapper.FromSummary(summary.LastIssue));
    }
}
