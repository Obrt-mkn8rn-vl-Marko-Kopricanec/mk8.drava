namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyTrafficMetricsSnapshot(long Requests, long BytesRead, long BytesWritten)
{
    public long Requests { get; } = MetricsList.RequireCounter(Requests, nameof(Requests));
    public long BytesRead { get; } = MetricsList.RequireCounter(BytesRead, nameof(BytesRead));
    public long BytesWritten { get; } = MetricsList.RequireCounter(BytesWritten, nameof(BytesWritten));
}
