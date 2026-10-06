using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ProxyConfigLintRuntimeConfigurationSourceMapper
{
    public static ProxyConfigLintRuntimeConfigurationSource FromSources(IEnumerable<string> sourceFiles, IEnumerable<string> adminUrls, bool adminRequiresAuthentication, bool publicMetricsEnabled, IEnumerable<RuntimeListener> listeners, IEnumerable<RuntimeRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(adminUrls);
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(routes);
        var listenerSources = ConfigLintList.Copy(listeners);
        var routeSources = ConfigLintList.Copy(routes);
        return new ProxyConfigLintRuntimeConfigurationSource(sourceFiles, adminUrls, adminRequiresAuthentication, publicMetricsEnabled, ProxyHttp3SupportConfigurationSourceMapper.FromSources(listenerSources, routeSources), listenerSources.Select(ToListenerSource), routeSources.Select(ToRouteSource));
    }

    private static ProxyConfigLintRuntimeListenerSource ToListenerSource(RuntimeListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new ProxyConfigLintRuntimeListenerSource(listener.Name, listener.Address, listener.Port, listener.Enabled, RuntimeListenerTransportText.FromTransport(listener.Transport), listener.Http3.Configured, listener.Http3.EnabledForTraffic, listener.Http3.DisabledReason, listener.Http3.EnablementLevel, Http3AltSvcListenerPolicy.IsEnabled(new Http3AltSvcListenerInput(listener.Http3.EnabledForTraffic, listener.Http3.EnablementLevel, listener.Http3AltSvc.Enabled, listener.Http3AltSvc.MaxAgeSeconds, listener.Port, listener.QuicIdentity?.Key)), listener.QuicIdentity?.Key);
    }

    private static ProxyConfigLintRuntimeRouteSource ToRouteSource(RuntimeRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return new ProxyConfigLintRuntimeRouteSource(route.Name, route.SiteName, route.Host, route.PathPrefix, ProxyRouteActionKindMapper.FromRuntimeActionText(route.Action), route.HttpsRedirect.Enabled, route.CanonicalHost.Enabled, route.CanonicalHost.TargetHost, route.Cache.Enabled, route.Cache.VaryByHeaders, route.Retry.Enabled, route.Retry.RetryMethods, route.HealthCheck.Enabled, route.Upstreams.Select(ToUpstreamSource), route.StaticResponse.Body);
    }

    private static ProxyConfigLintRuntimeUpstreamSource ToUpstreamSource(RuntimeUpstream upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        return new ProxyConfigLintRuntimeUpstreamSource(upstream.Name, upstream.Scheme, upstream.Protocol, upstream.Tls.ValidateCertificate, upstream.CircuitBreaker.Enabled);
    }
}
