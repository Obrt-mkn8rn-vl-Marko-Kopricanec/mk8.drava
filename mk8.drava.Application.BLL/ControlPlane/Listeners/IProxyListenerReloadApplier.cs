using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public interface IProxyListenerReloadApplier
{
    ValueTask<ProxyListenerReloadResult> ApplyReloadAsync(ProxyConfigurationSnapshot snapshot, Func<ProxyConfigurationSnapshot, ProxyConfigurationSnapshot> activateSnapshot, CancellationToken cancellationToken);
}
