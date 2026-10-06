using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.INF.Observability;
public sealed class ProxyConfigurationMetricsExportConfigurationSource : IProxyMetricsExportConfigurationSource
{
    private readonly IProxyActiveConfigurationSnapshotReader _configurationStore;
    public ProxyConfigurationMetricsExportConfigurationSource(IProxyActiveConfigurationSnapshotReader configurationStore)
    {
        _configurationStore = configurationStore;
    }

    public ProxyMetricsExportConfigurationReadResult ReadConfiguration()
    {
        var snapshotResult = _configurationStore.ReadSnapshot();
        if (snapshotResult is not ProxyConfigurationSnapshotReadResult.AvailableResult available)
        {
            return ProxyMetricsExportConfigurationReadResult.MissingConfiguration;
        }

        var snapshot = available.Snapshot;
        return ProxyMetricsExportConfigurationReadResult.Available(ProxyMetricsExportConfigurationMapper.FromSources(snapshot.Metrics.Enabled, ProxyMetricsExportLabelOptionsMapper.FromMetrics(snapshot.Metrics), ProxyMetricsExportHttp3FactsMapper.FromSources(snapshot.Listeners, snapshot.Routes)));
    }
}
