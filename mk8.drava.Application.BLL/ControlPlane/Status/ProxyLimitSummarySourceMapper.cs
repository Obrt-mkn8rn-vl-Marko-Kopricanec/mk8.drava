using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyLimitSummarySourceMapper
{
    public static ProxyLimitRuntimeSummarySource FromSources(long activeConnections, long activeTlsHandshakes, long activeHttp2Streams, long activeHttp3Streams, long activeUpstreamHttp3Streams)
    {
        return new ProxyLimitRuntimeSummarySource(activeConnections, activeTlsHandshakes, activeHttp2Streams, activeHttp3Streams, activeUpstreamHttp3Streams);
    }
}
