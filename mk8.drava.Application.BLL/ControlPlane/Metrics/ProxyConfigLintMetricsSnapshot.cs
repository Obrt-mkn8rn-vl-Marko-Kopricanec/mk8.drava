using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyConfigLintMetricsSnapshot
{
    public ProxyConfigLintMetricsSnapshot(long Runs, IEnumerable<ProxyConfigLintFindingMetricSnapshot> Findings)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Runs);
        this.Runs = Runs;
        this.Findings = MetricsList.Copy(Findings);
    }

    public long Runs { get; }
    public IReadOnlyList<ProxyConfigLintFindingMetricSnapshot> Findings { get; }
}
