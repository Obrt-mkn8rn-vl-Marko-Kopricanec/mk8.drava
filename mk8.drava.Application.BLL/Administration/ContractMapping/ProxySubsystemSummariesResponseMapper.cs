using BusinessProxySubsystemSummaries = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxySubsystemSummaries;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxySubsystemSummariesResponseMapper
{
    public static ProxySubsystemSummariesResponse FromSummaries(BusinessProxySubsystemSummaries summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        return new ProxySubsystemSummariesResponse(ProxyConfigSubsystemSummaryResponseMapper.FromSummary(summaries.Config), ProxyListenerSubsystemSummaryResponseMapper.FromSummary(summaries.Listeners), ProxyRouteSubsystemSummaryResponseMapper.FromSummary(summaries.Routes), ProxyCertificateSubsystemSummaryResponseMapper.FromSummary(summaries.Certificates), ProxyAcmeSubsystemSummaryResponseMapper.FromSummary(summaries.Acme), ProxyUpstreamSubsystemSummaryResponseMapper.FromSummary(summaries.Upstreams), ProxyCacheSubsystemSummaryResponseMapper.FromSummary(summaries.Cache), ProxyCircuitSubsystemSummaryResponseMapper.FromSummary(summaries.Circuits), ProxyLimitSubsystemSummaryResponseMapper.FromSummary(summaries.Limits), ProxyLogSubsystemSummaryResponseMapper.FromSummary(summaries.Logs), ProxyShutdownSubsystemSummaryResponseMapper.FromSummary(summaries.Shutdown), ProxyProtocolSubsystemSummaryResponseMapper.FromSummary(summaries.Protocols));
    }
}
