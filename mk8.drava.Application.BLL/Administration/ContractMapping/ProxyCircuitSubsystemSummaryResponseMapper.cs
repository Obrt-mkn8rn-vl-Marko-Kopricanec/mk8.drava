using BusinessProxyCacheSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyCacheSubsystemSummary;
using BusinessProxyCircuitSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyCircuitSubsystemSummary;
using BusinessProxyConfigSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyConfigSubsystemSummary;
using BusinessProxyLimitSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyLimitSubsystemSummary;
using BusinessProxyListenerSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyListenerSubsystemSummary;
using BusinessProxyLogSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyLogSubsystemSummary;
using BusinessProxyProtocolSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyProtocolSubsystemSummary;
using BusinessProxyRouteSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyRouteSubsystemSummary;
using BusinessProxyShutdownSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyShutdownSubsystemSummary;
using BusinessProxyUpstreamSubsystemSummary = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyUpstreamSubsystemSummary;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyCircuitSubsystemSummaryResponseMapper
{
    public static ProxyCircuitSubsystemSummaryResponse FromSummary(BusinessProxyCircuitSubsystemSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new ProxyCircuitSubsystemSummaryResponse(summary.Enabled, summary.Open, summary.HalfOpen, summary.Closed);
    }
}
