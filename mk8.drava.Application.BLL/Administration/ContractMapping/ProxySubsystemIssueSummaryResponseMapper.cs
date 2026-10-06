using BusinessProxyAcmeSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyAcmeSubsystemSummary;
using BusinessProxyCertificateSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyCertificateSubsystemSummary;
using BusinessProxySubsystemIssueSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxySubsystemIssueSummary;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxySubsystemIssueSummaryResponseMapper
{
    public static ProxySubsystemIssueSummaryResponse FromSummary(BusinessProxySubsystemIssueSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new ProxySubsystemIssueSummaryResponse(summary.TimestampUtc, summary.Category, summary.Reason, summary.AffectedIdentity);
    }
}
