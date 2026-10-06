using BusinessProxyListenerReloadChange = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyListenerReloadChange;
using BusinessProxyListenerReloadResult = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyListenerReloadResult;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyListenerReloadResponseMapper
{
    public static ProxyListenerReloadResponse FromResult(BusinessProxyListenerReloadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            BusinessProxyListenerReloadResult.AppliedResult applied => FromResult(applied, succeeded: true),
            BusinessProxyListenerReloadResult.FailedResult failed => FromResult(failed, succeeded: false),
            _ => throw new InvalidOperationException($"Unknown listener reload result '{result.GetType().Name}'.")};
    }

    private static ProxyListenerReloadResponse FromResult(BusinessProxyListenerReloadResult result, bool succeeded)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ProxyListenerReloadResponse(succeeded: succeeded, attemptedAtUtc: result.AttemptedAtUtc, added: result.Added, removed: result.Removed, changed: result.Changed, unchanged: result.Unchanged, changes: ProxyListenerReloadChangeResponseMapper.FromChanges(result.Changes), errors: result.Errors);
    }
}
