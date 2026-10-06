using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public static class Http3SupportSourceMapper
{
    public static IReadOnlyList<Http3SupportRuntimeListenerSource> FromListenerStatuses(IEnumerable<ProxyListenerStatus> listeners)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        return Http3List.Copy(listeners.Select(ToSource));
    }

    private static Http3SupportRuntimeListenerSource ToSource(ProxyListenerStatus listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new Http3SupportRuntimeListenerSource(string.Equals(listener.Kind, "quic", StringComparison.OrdinalIgnoreCase), listener.Identity, listener.State);
    }
}
