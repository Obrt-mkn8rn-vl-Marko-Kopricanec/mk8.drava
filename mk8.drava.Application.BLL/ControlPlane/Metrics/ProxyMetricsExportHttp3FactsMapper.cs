using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public static class ProxyMetricsExportHttp3FactsMapper
{
    public static ProxyMetricsExportHttp3Facts FromSources(IEnumerable<RuntimeListener> listeners, IEnumerable<RuntimeRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(routes);
        var defaultEnabledListenerCount = 0;
        var requestBodyStreamingEnabled = false;
        foreach (var listener in listeners)
        {
            if (!listener.Http3.EnabledForTraffic)
            {
                continue;
            }

            requestBodyStreamingEnabled = true;
            if (string.Equals(listener.Http3.EnablementLevel, "default", StringComparison.OrdinalIgnoreCase))
            {
                defaultEnabledListenerCount++;
            }
        }

        var upstreamMultiplexingConfigured = false;
        foreach (var route in routes)
        {
            if (route.Upstreams.Any(static upstream => RuntimeUpstreamProtocol.IsHttp3(upstream.Protocol)))
            {
                upstreamMultiplexingConfigured = true;
                break;
            }
        }

        return new ProxyMetricsExportHttp3Facts(defaultEnabledListenerCount, requestBodyStreamingEnabled, upstreamMultiplexingConfigured);
    }
}
