using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public static class ListenerProtocolAdvertisementInputMapper
{
    public static TcpAlpnAdvertisementInput FromTcpRuntimeProtocols(RuntimeListenerProtocols protocols)
    {
        return new TcpAlpnAdvertisementInput(protocols.HasFlag(RuntimeListenerProtocols.Http1), protocols.HasFlag(RuntimeListenerProtocols.Http2));
    }

    public static Http3AlpnAdvertisementInput FromHttp3RuntimeListener(RuntimeListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new Http3AlpnAdvertisementInput(listener.Http3.EnabledForTraffic);
    }
}
