using BusinessProxyListenerHttp3Status = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyListenerHttp3Status;
using BusinessProxyListenerState = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyListenerState;
using BusinessProxyQuicListenerIdentity = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyQuicListenerIdentity;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyListenerHttp3StatusResponseMapper
{
    public static ProxyListenerHttp3StatusResponse FromStatus(BusinessProxyListenerHttp3Status status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProxyListenerHttp3StatusResponse(status.Configured, status.DefaultEnabled, status.EnablementLevel, status.EnabledForTraffic, status.DisabledReason, status.AltSvcConfigured, status.AltSvcMaxAgeSeconds, status.UdpQuicListenerIdentityModeled, status.QuicIdentity is null ? null : ProxyQuicListenerIdentityResponseMapper.FromIdentity(status.QuicIdentity));
    }
}
