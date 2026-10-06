using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.INF.NoConf;
using Mk8.Drava.Application.INF.Runtime;
using Mk8.Drava.Configuration;
using System.Text.Json;

namespace Mk8.Drava.Application.Hosting;

internal sealed class GatewayPlanCoordinator(ApplicationBootstrap bootstrap, ProxyConfigurationStore store, ServingPlanState? plans, NoConfReconciler? reconciler) : IProxyListenerReloadApplier
{
    public async ValueTask<ProxyListenerReloadResult> ApplyReloadAsync(ProxyConfigurationSnapshot snapshot,
        Func<ProxyConfigurationSnapshot, ProxyConfigurationSnapshot> activateSnapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(activateSnapshot);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (bootstrap.Controller is not null && plans?.IsAcknowledged != true) throw new InvalidDataException("Gateway has not acknowledged its serving plan.");
            var current = store.Snapshot;
            ValidateListenerScope(snapshot, current);
            var baseline = snapshot.WithAdminSecurity(current.AdminSecurity).WithListenersAndRoutes(current.Listeners, snapshot.Routes);
            if (reconciler is null) activateSnapshot(baseline.WithVersion(checked(current.Version + 1)));
            else await reconciler.ApplyManualBaselineAsync(baseline, activateSnapshot, cancellationToken).ConfigureAwait(false);
            return ProxyListenerReloadResult.Applied(DateTimeOffset.UtcNow, 0, 0, 0, current.Listeners.Count, [], []);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or JsonException or IOException)
        {
            return ProxyListenerReloadResult.Failed(DateTimeOffset.UtcNow, 0, 0, 0, 0, [], ["Manual revision failed validation or changes the Gateway listener scope."]);
        }
    }

    private static void ValidateListenerScope(ProxyConfigurationSnapshot candidate, ProxyConfigurationSnapshot current)
    {
        if (candidate.Certificates.Count > 0) throw new InvalidDataException("Manual serving certificates require an acknowledged Gateway plan update.");
        foreach (var listener in candidate.Listeners)
        {
            var matched = current.Listeners.FirstOrDefault(value => string.Equals(value.Name, listener.Name, StringComparison.Ordinal));
            if (matched is null || !string.Equals(JsonSerializer.Serialize(listener), JsonSerializer.Serialize(matched), StringComparison.Ordinal))
                throw new InvalidDataException("Manual listeners must match the established Gateway profile.");
        }
    }
}
