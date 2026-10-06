using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public static class Http3AltSvcListenerPolicy
{
    public static bool IsEnabled(Http3AltSvcListenerInput listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return listener.EnabledForTraffic && (listener.AltSvcEnabled || string.Equals(listener.EnablementLevel, "default", StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasActiveQuicListener(Http3AltSvcListenerInput listener, IReadOnlyList<ProxyListenerStatus> runtimeListeners)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return !string.IsNullOrWhiteSpace(listener.QuicListenerIdentity) && runtimeListeners.Any(candidate => string.Equals(candidate.Kind, "quic", StringComparison.OrdinalIgnoreCase) && candidate.State == ProxyListenerState.Active && string.Equals(candidate.Identity, listener.QuicListenerIdentity, StringComparison.OrdinalIgnoreCase));
    }
}
