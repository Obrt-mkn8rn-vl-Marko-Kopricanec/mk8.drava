using BusinessProxyListenerHttp3Status = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyListenerHttp3Status;
using BusinessProxyListenerState = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyListenerState;
using BusinessProxyQuicListenerIdentity = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyQuicListenerIdentity;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyListenerStateResponseMapper
{
    public static ProxyListenerStateResponse FromState(BusinessProxyListenerState state)
    {
        return state switch
        {
            BusinessProxyListenerState.Starting => ProxyListenerStateResponse.Starting,
            BusinessProxyListenerState.Active => ProxyListenerStateResponse.Active,
            BusinessProxyListenerState.Draining => ProxyListenerStateResponse.Draining,
            BusinessProxyListenerState.Stopped => ProxyListenerStateResponse.Stopped,
            BusinessProxyListenerState.Failed => ProxyListenerStateResponse.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)};
    }
}
