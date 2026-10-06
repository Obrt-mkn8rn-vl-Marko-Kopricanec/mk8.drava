using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.Hosting;

internal sealed class GatewayPlanCoordinator : IProxyListenerReloadApplier
{
    public ValueTask<ProxyListenerReloadResult> ApplyReloadAsync(ProxyConfigurationSnapshot snapshot,
        Func<ProxyConfigurationSnapshot, ProxyConfigurationSnapshot> activateSnapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(activateSnapshot);
        cancellationToken.ThrowIfCancellationRequested();
        // Never return an applied result before the independent Gateway has acknowledged its listener plan.
        return ValueTask.FromResult(ProxyListenerReloadResult.Failed(DateTimeOffset.UtcNow, 0, 0, 0, 0, [], ["Gateway plan acknowledgment is not available for this reload."]));
    }
}
