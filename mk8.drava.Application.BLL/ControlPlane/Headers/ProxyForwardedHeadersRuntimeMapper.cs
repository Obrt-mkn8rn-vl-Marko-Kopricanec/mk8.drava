using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Headers;
public static class ProxyForwardedHeadersRuntimeMapper
{
    public static ForwardedHeadersListener ToListener(RuntimeListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new ForwardedHeadersListener(RuntimeListenerTransportScheme.FromTransport(listener.Transport), listener.Port);
    }
}
