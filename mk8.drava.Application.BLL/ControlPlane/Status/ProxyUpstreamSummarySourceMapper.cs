using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyUpstreamSummarySourceMapper
{
    public static IReadOnlyList<ProxyUpstreamSummarySource> FromStatusResponses(IReadOnlyList<ProxyUpstreamStatus> upstreams)
    {
        ArgumentNullException.ThrowIfNull(upstreams);
        return ProxyStatusList.Copy(upstreams.Select(ToSource));
    }

    private static ProxyUpstreamSummarySource ToSource(ProxyUpstreamStatus upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        return new ProxyUpstreamSummarySource(upstream.HealthState, upstream.HealthCheckEnabled, upstream.CircuitBreaker.Enabled, upstream.CircuitBreaker.State);
    }
}
