using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyMetricsExportConfiguration(bool MetricsEnabled, ProxyMetricsExportLabelOptions LabelOptions, ProxyMetricsExportHttp3Facts Http3Facts);
