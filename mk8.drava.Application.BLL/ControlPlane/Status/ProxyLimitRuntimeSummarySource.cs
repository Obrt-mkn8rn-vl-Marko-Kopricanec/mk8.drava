using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyLimitRuntimeSummarySource
{
    public ProxyLimitRuntimeSummarySource(long ActiveConnections, long ActiveTlsHandshakes, long ActiveHttp2Streams, long ActiveHttp3Streams, long ActiveUpstreamHttp3Streams)
    {
        ProxyStatusFacts.RequireNonNegative(ActiveConnections, nameof(ActiveConnections));
        ProxyStatusFacts.RequireNonNegative(ActiveTlsHandshakes, nameof(ActiveTlsHandshakes));
        ProxyStatusFacts.RequireNonNegative(ActiveHttp2Streams, nameof(ActiveHttp2Streams));
        ProxyStatusFacts.RequireNonNegative(ActiveHttp3Streams, nameof(ActiveHttp3Streams));
        ProxyStatusFacts.RequireNonNegative(ActiveUpstreamHttp3Streams, nameof(ActiveUpstreamHttp3Streams));
        this.ActiveConnections = ActiveConnections;
        this.ActiveTlsHandshakes = ActiveTlsHandshakes;
        this.ActiveHttp2Streams = ActiveHttp2Streams;
        this.ActiveHttp3Streams = ActiveHttp3Streams;
        this.ActiveUpstreamHttp3Streams = ActiveUpstreamHttp3Streams;
    }

    public long ActiveConnections { get; }
    public long ActiveTlsHandshakes { get; }
    public long ActiveHttp2Streams { get; }
    public long ActiveHttp3Streams { get; }
    public long ActiveUpstreamHttp3Streams { get; }
}
