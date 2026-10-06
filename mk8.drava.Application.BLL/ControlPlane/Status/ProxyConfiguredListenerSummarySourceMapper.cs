using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyConfiguredListenerSummarySourceMapper
{
    public static IReadOnlyList<ProxyConfiguredListenerSummarySource> FromListeners(IEnumerable<RuntimeListener> listeners)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        return ProxyStatusList.Copy(listeners.Select(ToSource));
    }

    private static ProxyConfiguredListenerSummarySource ToSource(RuntimeListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new ProxyConfiguredListenerSummarySource(listener.Enabled, listener.Protocols.HasFlag(RuntimeListenerProtocols.Http1), listener.Protocols.HasFlag(RuntimeListenerProtocols.Http2), listener.Http3.EnabledForTraffic);
    }
}
