using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public static class ProxyHttp3AltSvcRuntimeMapper
{
    public static Http3AltSvcListenerInput ToListenerInput(RuntimeListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new Http3AltSvcListenerInput(listener.Http3.EnabledForTraffic, listener.Http3.EnablementLevel, listener.Http3AltSvc.Enabled, listener.Http3AltSvc.MaxAgeSeconds, listener.Port, listener.QuicIdentity?.Key);
    }
}
