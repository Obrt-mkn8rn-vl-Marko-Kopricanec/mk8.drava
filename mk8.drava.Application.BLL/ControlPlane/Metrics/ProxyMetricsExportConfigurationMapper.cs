using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public static class ProxyMetricsExportConfigurationMapper
{
    public static ProxyMetricsExportConfiguration FromSources(bool metricsEnabled, ProxyMetricsExportLabelOptions labelOptions, ProxyMetricsExportHttp3Facts http3Facts)
    {
        ArgumentNullException.ThrowIfNull(labelOptions);
        ArgumentNullException.ThrowIfNull(http3Facts);
        return new ProxyMetricsExportConfiguration(metricsEnabled, labelOptions, http3Facts);
    }
}
