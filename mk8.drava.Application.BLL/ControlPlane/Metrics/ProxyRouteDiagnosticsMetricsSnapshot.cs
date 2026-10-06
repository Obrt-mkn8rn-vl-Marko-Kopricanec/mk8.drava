using Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyRouteDiagnosticsMetricsSnapshot
{
    public ProxyRouteDiagnosticsMetricsSnapshot(long DryRuns, IEnumerable<ProxyRouteDryRunFailureSnapshot> DryRunFailures)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(DryRuns);
        this.DryRuns = DryRuns;
        this.DryRunFailures = MetricsList.Copy(DryRunFailures);
    }

    public long DryRuns { get; }
    public IReadOnlyList<ProxyRouteDryRunFailureSnapshot> DryRunFailures { get; }
}
