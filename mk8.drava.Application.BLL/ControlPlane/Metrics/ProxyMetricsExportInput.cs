using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyMetricsExportInput
{
    public ProxyMetricsExportInput(ProxyMetricsSnapshot Metrics, bool IncludePerRouteLabels, bool IncludePerUpstreamLabels, int DefaultEnabledHttp3ListenerCount, bool Http3RequestBodyStreamingEnabled, bool UpstreamHttp3MultiplexingConfigured, ProxyCacheStatus CacheStatus, IReadOnlyList<ProxyUpstreamStatus> UpstreamHealth, IReadOnlyList<AcmeCertificateLifecycleStatus> AcmeCertificates)
    {
        ArgumentNullException.ThrowIfNull(Metrics);
        ArgumentNullException.ThrowIfNull(CacheStatus);
        ArgumentOutOfRangeException.ThrowIfNegative(DefaultEnabledHttp3ListenerCount, nameof(DefaultEnabledHttp3ListenerCount));
        this.Metrics = Metrics;
        this.IncludePerRouteLabels = IncludePerRouteLabels;
        this.IncludePerUpstreamLabels = IncludePerUpstreamLabels;
        this.DefaultEnabledHttp3ListenerCount = DefaultEnabledHttp3ListenerCount;
        this.Http3RequestBodyStreamingEnabled = Http3RequestBodyStreamingEnabled;
        this.UpstreamHttp3MultiplexingConfigured = UpstreamHttp3MultiplexingConfigured;
        this.CacheStatus = CacheStatus;
        this.UpstreamHealth = MetricsList.Copy(UpstreamHealth);
        this.AcmeCertificates = MetricsList.Copy(AcmeCertificates);
    }

    public ProxyMetricsSnapshot Metrics { get; }
    public bool IncludePerRouteLabels { get; }
    public bool IncludePerUpstreamLabels { get; }
    public int DefaultEnabledHttp3ListenerCount { get; }
    public bool Http3RequestBodyStreamingEnabled { get; }
    public bool UpstreamHttp3MultiplexingConfigured { get; }
    public ProxyCacheStatus CacheStatus { get; }
    public IReadOnlyList<ProxyUpstreamStatus> UpstreamHealth { get; }
    public IReadOnlyList<AcmeCertificateLifecycleStatus> AcmeCertificates { get; }
}
