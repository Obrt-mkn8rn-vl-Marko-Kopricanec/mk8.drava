using BusinessProxyListenerReloadChange = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyListenerReloadChange;
using BusinessProxyListenerReloadResult = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyListenerReloadResult;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyListenerReloadChangeResponseMapper
{
    public static IReadOnlyList<ProxyListenerReloadChangeResponse> FromChanges(IReadOnlyList<BusinessProxyListenerReloadChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return ApiResponseList.Copy(changes.Select(FromChange));
    }

    private static ProxyListenerReloadChangeResponse FromChange(BusinessProxyListenerReloadChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return new ProxyListenerReloadChangeResponse(change.Action, change.Name, change.Identity, change.BindKey, change.State, change.Error);
    }
}
