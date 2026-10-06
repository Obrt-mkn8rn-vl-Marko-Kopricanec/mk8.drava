namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyUpstreamSelectionSnapshot(string Route, string Upstream, string Scheme, string Protocol, long Count)
{
    public long Count { get; } = MetricsList.RequireCounter(Count, nameof(Count));
}
