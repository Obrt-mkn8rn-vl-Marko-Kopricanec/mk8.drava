using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.INF.Proxy.ConfigLint;
public sealed class ProxyConfigLintRuntimeStateSource : IProxyConfigLintRuntimeStateSource
{
    private readonly ProxyRuntimeState _runtimeState;
    public ProxyConfigLintRuntimeStateSource(ProxyRuntimeState runtimeState)
    {
        _runtimeState = runtimeState;
    }

    public IReadOnlyList<ProxyConfigLintRuntimeListenerState> GetListenerStates()
    {
        return ProxyConfigLintRuntimeListenerStateMapper.FromListenerStatuses(_runtimeState.Snapshot().Listeners);
    }
}
