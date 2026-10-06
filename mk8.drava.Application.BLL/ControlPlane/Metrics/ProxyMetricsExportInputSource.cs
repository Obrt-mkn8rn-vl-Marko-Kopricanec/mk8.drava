using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed class ProxyMetricsExportInputSource : IProxyMetricsExportInputSource
{
    private readonly IProxyMetricsExportConfigurationSource _configurationSource;
    private readonly IProxyStatusMetricsSource _metricsSource;
    private readonly IProxyCacheStatusReader _cacheStatusReader;
    private readonly IProxyStatusUpstreamHealthReader _upstreamHealthReader;
    private readonly IProxyAcmeCertificateLifecycleStatusSource _acmeStatusSource;
    public ProxyMetricsExportInputSource(IProxyMetricsExportConfigurationSource configurationSource, IProxyStatusMetricsSource metricsSource, IProxyCacheStatusReader cacheStatusReader, IProxyStatusUpstreamHealthReader upstreamHealthReader, IProxyAcmeCertificateLifecycleStatusSource acmeStatusSource)
    {
        _configurationSource = configurationSource;
        _metricsSource = metricsSource;
        _cacheStatusReader = cacheStatusReader;
        _upstreamHealthReader = upstreamHealthReader;
        _acmeStatusSource = acmeStatusSource;
    }

    public ProxyMetricsExportInputReadResult ReadInput()
    {
        var configurationResult = _configurationSource.ReadConfiguration();
        if (configurationResult is not ProxyMetricsExportConfigurationReadResult.AvailableResult available)
        {
            return ProxyMetricsExportInputReadResult.MissingConfiguration;
        }

        var configuration = available.Configuration;
        return ProxyMetricsExportInputReadResult.Available(ProxyMetricsExportInputMapper.FromSources(_metricsSource.ReadMetrics(), configuration.LabelOptions, configuration.Http3Facts, _cacheStatusReader.GetStatus(), _upstreamHealthReader.ReadUpstreams(), _acmeStatusSource.GetLifecycleStatuses()));
    }
}
