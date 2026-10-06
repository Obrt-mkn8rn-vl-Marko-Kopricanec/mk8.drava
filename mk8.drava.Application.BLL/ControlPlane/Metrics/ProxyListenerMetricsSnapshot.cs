namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyListenerMetricsSnapshot(long ReloadAttempts, long ReloadSuccesses, long ReloadFailures, long ReloadAdded, long ReloadRemoved, long ReloadChanged, long ReloadUnchanged, long StartFailures, long Drains, long ActiveListeners);
