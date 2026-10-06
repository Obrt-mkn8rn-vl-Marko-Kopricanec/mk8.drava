using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public static class ProxyMetricsExportInputMapper
{
    public static ProxyMetricsExportInput FromSources(ProxyMetricsSnapshot metrics, ProxyMetricsExportLabelOptions labelOptions, ProxyMetricsExportHttp3Facts http3Facts, ProxyCacheStatus cacheStatus, IReadOnlyList<ProxyUpstreamStatus> upstreamHealth, IReadOnlyList<AcmeCertificateLifecycleStatus> acmeCertificates)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(labelOptions);
        ArgumentNullException.ThrowIfNull(http3Facts);
        ArgumentNullException.ThrowIfNull(cacheStatus);
        ArgumentNullException.ThrowIfNull(upstreamHealth);
        ArgumentNullException.ThrowIfNull(acmeCertificates);
        return new ProxyMetricsExportInput(metrics, labelOptions.IncludePerRouteLabels, labelOptions.IncludePerUpstreamLabels, http3Facts.DefaultEnabledListenerCount, http3Facts.RequestBodyStreamingEnabled, http3Facts.UpstreamMultiplexingConfigured, cacheStatus, upstreamHealth, acmeCertificates);
    }
}
