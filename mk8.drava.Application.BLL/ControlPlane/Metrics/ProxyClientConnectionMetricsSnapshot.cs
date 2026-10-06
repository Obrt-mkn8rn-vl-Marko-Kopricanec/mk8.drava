namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyClientConnectionMetricsSnapshot(long Accepted, long Active, long ClosedByIdleTimeout, long ClosedByMaxRequests)
{
    public long Accepted { get; } = MetricsList.RequireCounter(Accepted, nameof(Accepted));
    public long Active { get; } = MetricsList.RequireCounter(Active, nameof(Active));
    public long ClosedByIdleTimeout { get; } = MetricsList.RequireCounter(ClosedByIdleTimeout, nameof(ClosedByIdleTimeout));
    public long ClosedByMaxRequests { get; } = MetricsList.RequireCounter(ClosedByMaxRequests, nameof(ClosedByMaxRequests));
}
