using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ProxyConfigurationReloadResultAssertions
{
    public static ProxyConfigurationReloadResult<TProjection>.ReloadedResult Reloaded<TProjection>(ProxyConfigurationReloadResult<TProjection> result, string? message = null)
        where TProjection : class
    {
        if (result is not ProxyConfigurationReloadResult<TProjection>.ReloadedResult reloaded)
        {
            throw new InvalidOperationException(message ?? "Expected configuration reload to succeed.");
        }

        return reloaded;
    }

    public static ProxyConfigurationReloadResult<TProjection>.LoadFailedResult LoadFailed<TProjection>(ProxyConfigurationReloadResult<TProjection> result, string? message = null)
        where TProjection : class
    {
        if (result is not ProxyConfigurationReloadResult<TProjection>.LoadFailedResult loadFailed)
        {
            throw new InvalidOperationException(message ?? "Expected configuration reload to fail while loading configuration.");
        }

        return loadFailed;
    }

    public static ProxyConfigurationReloadResult<TProjection>.ListenerReloadFailedResult ListenerReloadFailed<TProjection>(ProxyConfigurationReloadResult<TProjection> result, string? message = null)
        where TProjection : class
    {
        if (result is not ProxyConfigurationReloadResult<TProjection>.ListenerReloadFailedResult listenerReloadFailed)
        {
            throw new InvalidOperationException(message ?? "Expected configuration reload to fail while applying listeners.");
        }

        return listenerReloadFailed;
    }

    public static void Failed<TProjection>(ProxyConfigurationReloadResult<TProjection> result, string? message = null)
        where TProjection : class
    {
        if (result is not ProxyConfigurationReloadResult<TProjection>.LoadFailedResult && result is not ProxyConfigurationReloadResult<TProjection>.ListenerReloadFailedResult)
        {
            throw new InvalidOperationException(message ?? "Expected configuration reload to fail.");
        }
    }
}
