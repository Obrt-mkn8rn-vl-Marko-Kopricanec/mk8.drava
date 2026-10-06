using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public static class ProxyHttp3RequestTranslationRuntimeMapper
{
    public static Http3RequestTranslationListenerInput ToListenerInput(RuntimeListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new Http3RequestTranslationListenerInput(listener.Transport == RuntimeListenerTransport.Https);
    }
}
