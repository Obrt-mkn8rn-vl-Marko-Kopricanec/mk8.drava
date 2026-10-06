using BusinessProxyConfigurationProjection = Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationProjection;
using BusinessProxyConfigurationReloadResult = Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationReloadResult<Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationProjection>;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyConfigurationReloadResponseMapper
{
    public static ProxyConfigurationReloadResponse FromResult(BusinessProxyConfigurationReloadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            BusinessProxyConfigurationReloadResult.LoadFailedResult loadFailed => FromResult(loadFailed, succeeded: false, activeConfiguration: loadFailed.ActiveConfiguration, listenerReload: null),
            BusinessProxyConfigurationReloadResult.ListenerReloadFailedResult listenerReloadFailed => FromResult(listenerReloadFailed, succeeded: false, activeConfiguration: listenerReloadFailed.ActiveConfiguration, listenerReload: ProxyListenerReloadResponseMapper.FromResult(listenerReloadFailed.ListenerReload)),
            BusinessProxyConfigurationReloadResult.ReloadedResult reloaded => FromResult(reloaded, succeeded: true, activeConfiguration: reloaded.ActiveConfiguration, listenerReload: ProxyListenerReloadResponseMapper.FromResult(reloaded.ListenerReload)),
            _ => throw new InvalidOperationException($"Unknown reload result '{result.GetType().Name}'.")};
    }

    private static ProxyConfigurationReloadResponse FromResult(BusinessProxyConfigurationReloadResult result, bool succeeded, BusinessProxyConfigurationProjection? activeConfiguration, ProxyListenerReloadResponse? listenerReload)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ProxyConfigurationReloadResponse(succeeded: succeeded, sourceDirectory: result.SourceDirectory, attemptedAtUtc: result.AttemptedAtUtc, activeVersion: result.ActiveVersion, loadedAtUtc: result.LoadedAtUtc, lastSuccessfulLoadAtUtc: result.LastSuccessfulLoadAtUtc, discovery: ProxyConfigurationDiscoveryResponseMapper.FromDiscovery(result.Discovery), errors: result.Errors, fileErrors: ProxyConfigurationFileErrorResponseMapper.FromErrors(result.FileErrors), activeConfiguration: activeConfiguration is null ? null : ProxyConfigurationResponseMapper.FromProjection(activeConfiguration), listenerReload: listenerReload);
    }
}
