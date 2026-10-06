using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public interface IProxyConfigLintRuntimeStateSource
{
    IReadOnlyList<ProxyConfigLintRuntimeListenerState> GetListenerStates();
}
