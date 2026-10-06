namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyRequestSeriesSnapshot(string Site, string Route, string Action, string StatusClass, long Count)
{
    public long Count { get; } = MetricsList.RequireCounter(Count, nameof(Count));
}
