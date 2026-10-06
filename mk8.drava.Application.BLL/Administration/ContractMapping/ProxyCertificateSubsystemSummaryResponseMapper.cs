using BusinessProxyAcmeSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyAcmeSubsystemSummary;
using BusinessProxyCertificateSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyCertificateSubsystemSummary;
using BusinessProxySubsystemIssueSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxySubsystemIssueSummary;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyCertificateSubsystemSummaryResponseMapper
{
    public static ProxyCertificateSubsystemSummaryResponse FromSummary(BusinessProxyCertificateSubsystemSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new ProxyCertificateSubsystemSummaryResponse(summary.Configured, summary.Loaded, summary.MissingReferences, summary.Expired, summary.NotYetValid, summary.ExpiringSoon, summary.LastIssue is null ? null : ProxySubsystemIssueSummaryResponseMapper.FromSummary(summary.LastIssue));
    }
}
