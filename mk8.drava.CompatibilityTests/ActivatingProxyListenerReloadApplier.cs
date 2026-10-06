using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal sealed class ActivatingProxyListenerReloadApplier : IProxyListenerReloadApplier
{
    public static ActivatingProxyListenerReloadApplier Instance { get; } = new();

    private ActivatingProxyListenerReloadApplier()
    {
    }

    public ValueTask<ProxyListenerReloadResult> ApplyReloadAsync(ProxyConfigurationSnapshot snapshot, Func<ProxyConfigurationSnapshot, ProxyConfigurationSnapshot> activateSnapshot, CancellationToken cancellationToken)
    {
        activateSnapshot(snapshot);
        return ValueTask.FromResult(ProxyListenerReloadResult.Applied(DateTimeOffset.UnixEpoch, added: 0, removed: 0, changed: 0, unchanged: 0, changes: [], errors: []));
    }
}
