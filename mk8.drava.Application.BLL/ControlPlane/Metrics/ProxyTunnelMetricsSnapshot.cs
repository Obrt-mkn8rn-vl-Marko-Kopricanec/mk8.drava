namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyTunnelMetricsSnapshot(long Active, long Total, long IdleTimeouts, long BytesClientToUpstream, long BytesUpstreamToClient, long RelayFailures)
{
    public long Active { get; } = MetricsList.RequireCounter(Active, nameof(Active));
    public long Total { get; } = MetricsList.RequireCounter(Total, nameof(Total));
    public long IdleTimeouts { get; } = MetricsList.RequireCounter(IdleTimeouts, nameof(IdleTimeouts));
    public long BytesClientToUpstream { get; } = MetricsList.RequireCounter(BytesClientToUpstream, nameof(BytesClientToUpstream));
    public long BytesUpstreamToClient { get; } = MetricsList.RequireCounter(BytesUpstreamToClient, nameof(BytesUpstreamToClient));
    public long RelayFailures { get; } = MetricsList.RequireCounter(RelayFailures, nameof(RelayFailures));
}
