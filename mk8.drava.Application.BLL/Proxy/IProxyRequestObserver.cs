using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;

namespace Mk8.Drava.Application.BLL.Proxy;

public interface IProxyRequestObserver
{
    void Complete(ProxyRequestContext context, ProxyConfigurationSnapshot snapshot);
}
