using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public interface IHttp3AltSvcRuntimeListenerSource
{
    IReadOnlyList<ProxyListenerStatus> ReadRuntimeListeners();
}
