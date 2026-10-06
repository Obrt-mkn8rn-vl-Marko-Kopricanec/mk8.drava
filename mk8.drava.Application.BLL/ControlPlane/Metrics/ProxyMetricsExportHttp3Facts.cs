using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyMetricsExportHttp3Facts
{
    public ProxyMetricsExportHttp3Facts(int DefaultEnabledListenerCount, bool RequestBodyStreamingEnabled, bool UpstreamMultiplexingConfigured)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(DefaultEnabledListenerCount, nameof(DefaultEnabledListenerCount));
        this.DefaultEnabledListenerCount = DefaultEnabledListenerCount;
        this.RequestBodyStreamingEnabled = RequestBodyStreamingEnabled;
        this.UpstreamMultiplexingConfigured = UpstreamMultiplexingConfigured;
    }

    public int DefaultEnabledListenerCount { get; }
    public bool RequestBodyStreamingEnabled { get; }
    public bool UpstreamMultiplexingConfigured { get; }
}
