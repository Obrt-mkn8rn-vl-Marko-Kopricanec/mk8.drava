namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyRequestClassificationMetricsSnapshot
{
    public ProxyRequestClassificationMetricsSnapshot(IReadOnlyDictionary<string, long> FailuresByKind, IEnumerable<ProxyRequestSeriesSnapshot> ByRoute)
    {
        this.FailuresByKind = MetricsList.CopyCounterDictionary(FailuresByKind, StringComparer.Ordinal);
        this.ByRoute = MetricsList.Copy(ByRoute);
    }

    public IReadOnlyDictionary<string, long> FailuresByKind { get; }
    public IReadOnlyList<ProxyRequestSeriesSnapshot> ByRoute { get; }
}
