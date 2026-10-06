using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ProxyConfigLintRuntimeListenerStateMapper
{
    public static IReadOnlyList<ProxyConfigLintRuntimeListenerState> FromListenerStatuses(IReadOnlyList<ProxyListenerStatus> listeners)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        return ConfigLintList.Copy(listeners.Select(ToListenerState));
    }

    private static ProxyConfigLintRuntimeListenerState ToListenerState(ProxyListenerStatus listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new ProxyConfigLintRuntimeListenerState(listener.Identity, listener.Kind, listener.State == ProxyListenerState.Active);
    }
}
