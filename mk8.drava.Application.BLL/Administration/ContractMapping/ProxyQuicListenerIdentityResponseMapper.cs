using BusinessProxyListenerHttp3Status = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyListenerHttp3Status;
using BusinessProxyListenerState = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyListenerState;
using BusinessProxyQuicListenerIdentity = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyQuicListenerIdentity;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyQuicListenerIdentityResponseMapper
{
    public static ProxyQuicListenerIdentityResponse FromIdentity(BusinessProxyQuicListenerIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new ProxyQuicListenerIdentityResponse(identity.Name, identity.Address, identity.Port, identity.TlsEnabled, identity.Key, identity.BindKey);
    }
}
