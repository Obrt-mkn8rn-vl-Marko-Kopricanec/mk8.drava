using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public static class ProxyHttp3SupportConfigurationSourceMapper
{
    public static Http3SupportConfigurationSource FromSources(IEnumerable<RuntimeListener> listeners, IEnumerable<RuntimeRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(routes);
        return new Http3SupportConfigurationSource(listeners.Select(ToListenerSource), routes.Any(HasHttp3Upstream));
    }

    private static Http3SupportListenerSource ToListenerSource(RuntimeListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new Http3SupportListenerSource(listener.Http3.Configured, listener.Http3.EnabledForTraffic, listener.Http3.EnablementLevel, Http3AltSvcListenerPolicy.IsEnabled(new Http3AltSvcListenerInput(listener.Http3.EnabledForTraffic, listener.Http3.EnablementLevel, listener.Http3AltSvc.Enabled, listener.Http3AltSvc.MaxAgeSeconds, listener.Port, listener.QuicIdentity?.Key)), listener.Http3AltSvc.MaxAgeSeconds, listener.QuicIdentity?.Key);
    }

    private static bool HasHttp3Upstream(RuntimeRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return route.Upstreams.Any(static upstream =>
        {
            ArgumentNullException.ThrowIfNull(upstream);
            return RuntimeUpstreamProtocol.IsHttp3(upstream.Protocol);
        });
    }
}
