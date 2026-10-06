using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyRuntimeListenerSummarySourceMapper
{
    public static IReadOnlyList<ProxyRuntimeListenerSummarySource> FromSources(IEnumerable<ProxyListenerStatus> listeners)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        return ProxyStatusList.Copy(listeners.Select(ToSource));
    }

    private static ProxyRuntimeListenerSummarySource ToSource(ProxyListenerStatus listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new ProxyRuntimeListenerSummarySource(string.Equals(listener.Kind, "quic", StringComparison.OrdinalIgnoreCase), listener.State);
    }
}
