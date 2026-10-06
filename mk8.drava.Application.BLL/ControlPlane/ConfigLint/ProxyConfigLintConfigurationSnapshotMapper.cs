using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ProxyConfigLintConfigurationSnapshotMapper
{
    public static ProxyConfigLintConfigurationSnapshot ToLintSnapshot(ProxyConfigLintRuntimeConfigurationSource source, RuntimeHttp3PlatformSupport platformSupport)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(platformSupport);
        var http3 = Http3RuntimeSupport.ProjectConfiguration(source.Http3Support, platformSupport);
        return new ProxyConfigLintConfigurationSnapshot(source.SourceFiles, new ProxyConfigLintAdminSecurity(source.AdminUrls, source.AdminRequiresAuthentication), new ProxyConfigLintMetricsOptions(source.PublicMetricsEnabled), http3.QuicConnectionSupported, source.Listeners.Select(ToLintListener), source.Routes.Select(ToLintRoute));
    }

    private static ProxyConfigLintListener ToLintListener(ProxyConfigLintRuntimeListenerSource listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new ProxyConfigLintListener(listener.Name, listener.Address, listener.Port, listener.Enabled, listener.Transport, listener.Http3Configured, listener.Http3EnabledForTraffic, listener.Http3DisabledReason, listener.Http3EnablementLevel, listener.Http3AltSvcEnabled, listener.QuicIdentityKey);
    }

    private static ProxyConfigLintRoute ToLintRoute(ProxyConfigLintRuntimeRouteSource route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return new ProxyConfigLintRoute(route.Name, route.SiteName, route.Host, route.PathPrefix, route.Action, route.HttpsRedirectEnabled, route.CanonicalHostEnabled, route.CanonicalHostTargetHost, route.CacheEnabled, route.CacheVaryByHeaders, route.RetryEnabled, route.RetryMethods, route.HealthCheckEnabled, route.Upstreams.Select(ToLintUpstream), route.StaticResponseBody);
    }

    private static ProxyConfigLintUpstream ToLintUpstream(ProxyConfigLintRuntimeUpstreamSource upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        return new ProxyConfigLintUpstream(upstream.Name, upstream.Scheme, upstream.Protocol, upstream.TlsValidateCertificate, upstream.CircuitBreakerEnabled);
    }
}
