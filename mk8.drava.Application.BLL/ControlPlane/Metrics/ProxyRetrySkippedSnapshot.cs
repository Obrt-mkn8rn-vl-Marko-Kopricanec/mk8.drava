namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyRetrySkippedSnapshot(string Reason, long Count)
{
    public long Count { get; } = MetricsList.RequireCounter(Count, nameof(Count));
}
