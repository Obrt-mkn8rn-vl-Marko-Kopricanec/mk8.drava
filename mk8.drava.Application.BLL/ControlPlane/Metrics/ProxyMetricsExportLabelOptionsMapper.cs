using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public static class ProxyMetricsExportLabelOptionsMapper
{
    public static ProxyMetricsExportLabelOptions FromMetrics(RuntimeMetricsOptions metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        return new ProxyMetricsExportLabelOptions(metrics.IncludePerRouteLabels, metrics.IncludePerUpstreamLabels);
    }
}
