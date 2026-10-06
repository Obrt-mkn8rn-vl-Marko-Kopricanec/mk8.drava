namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyUpstreamSelectionMetricsSnapshot
{
    public ProxyUpstreamSelectionMetricsSnapshot(long Total, IEnumerable<ProxyUpstreamSelectionSnapshot> ByUpstream)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Total);
        this.Total = Total;
        this.ByUpstream = MetricsList.Copy(ByUpstream);
    }

    public long Total { get; }
    public IReadOnlyList<ProxyUpstreamSelectionSnapshot> ByUpstream { get; }
}
