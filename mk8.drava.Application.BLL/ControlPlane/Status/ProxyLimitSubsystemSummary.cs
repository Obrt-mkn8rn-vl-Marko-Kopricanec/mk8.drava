namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyLimitSubsystemSummary
{
    public ProxyLimitSubsystemSummary(int MaxActiveClientConnections, long ActiveConnections, int MaxConcurrentTlsHandshakes, long ActiveTlsHandshakes, long ActiveHttp2Streams, long ActiveHttp3Streams, long ActiveUpstreamHttp3Streams, int RequestsPerMinutePerIp)
    {
        ProxyStatusFacts.RequireNonNegative(MaxActiveClientConnections, nameof(MaxActiveClientConnections));
        ProxyStatusFacts.RequireNonNegative(ActiveConnections, nameof(ActiveConnections));
        ProxyStatusFacts.RequireNonNegative(MaxConcurrentTlsHandshakes, nameof(MaxConcurrentTlsHandshakes));
        ProxyStatusFacts.RequireNonNegative(ActiveTlsHandshakes, nameof(ActiveTlsHandshakes));
        ProxyStatusFacts.RequireNonNegative(ActiveHttp2Streams, nameof(ActiveHttp2Streams));
        ProxyStatusFacts.RequireNonNegative(ActiveHttp3Streams, nameof(ActiveHttp3Streams));
        ProxyStatusFacts.RequireNonNegative(ActiveUpstreamHttp3Streams, nameof(ActiveUpstreamHttp3Streams));
        ProxyStatusFacts.RequireNonNegative(RequestsPerMinutePerIp, nameof(RequestsPerMinutePerIp));
        this.MaxActiveClientConnections = MaxActiveClientConnections;
        this.ActiveConnections = ActiveConnections;
        this.MaxConcurrentTlsHandshakes = MaxConcurrentTlsHandshakes;
        this.ActiveTlsHandshakes = ActiveTlsHandshakes;
        this.ActiveHttp2Streams = ActiveHttp2Streams;
        this.ActiveHttp3Streams = ActiveHttp3Streams;
        this.ActiveUpstreamHttp3Streams = ActiveUpstreamHttp3Streams;
        this.RequestsPerMinutePerIp = RequestsPerMinutePerIp;
    }

    public int MaxActiveClientConnections { get; }
    public long ActiveConnections { get; }
    public int MaxConcurrentTlsHandshakes { get; }
    public long ActiveTlsHandshakes { get; }
    public long ActiveHttp2Streams { get; }
    public long ActiveHttp3Streams { get; }
    public long ActiveUpstreamHttp3Streams { get; }
    public int RequestsPerMinutePerIp { get; }
    public static ProxyLimitSubsystemSummary Unknown { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}
