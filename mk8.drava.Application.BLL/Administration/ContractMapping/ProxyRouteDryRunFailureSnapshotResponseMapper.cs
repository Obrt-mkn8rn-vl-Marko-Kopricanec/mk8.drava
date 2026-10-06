using BusinessProxyConfigLintFindingMetricSnapshot = Mk8.Drava.Application.BLL.ControlPlane.ConfigLint.ProxyConfigLintFindingMetricSnapshot;
using BusinessProxyHttp3RequestOutcomeSnapshot = Mk8.Drava.Application.BLL.ControlPlane.Metrics.ProxyHttp3RequestOutcomeSnapshot;
using BusinessProxyRequestSeriesSnapshot = Mk8.Drava.Application.BLL.ControlPlane.Metrics.ProxyRequestSeriesSnapshot;
using BusinessProxyRetrySkippedSnapshot = Mk8.Drava.Application.BLL.ControlPlane.Metrics.ProxyRetrySkippedSnapshot;
using BusinessProxyRouteDryRunFailureSnapshot = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.ProxyRouteDryRunFailureSnapshot;
using BusinessProxyUpstreamSelectionSnapshot = Mk8.Drava.Application.BLL.ControlPlane.Metrics.ProxyUpstreamSelectionSnapshot;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyRouteDryRunFailureSnapshotResponseMapper
{
    public static IReadOnlyList<ProxyRouteDryRunFailureSnapshotResponse> FromSnapshots(IReadOnlyList<BusinessProxyRouteDryRunFailureSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        return ApiResponseList.Copy(snapshots.Select(FromSnapshot));
    }

    private static ProxyRouteDryRunFailureSnapshotResponse FromSnapshot(BusinessProxyRouteDryRunFailureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ProxyRouteDryRunFailureSnapshotResponse(snapshot.Reason, snapshot.Count);
    }
}
