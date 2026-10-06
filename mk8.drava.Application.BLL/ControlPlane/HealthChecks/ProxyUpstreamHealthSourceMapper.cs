using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;

namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public static class ProxyUpstreamHealthSourceMapper
{
    public static IReadOnlyList<ProxyUpstreamHealthSource> FromRoutes(IEnumerable<RuntimeRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        return HealthCheckList.Copy(routes.SelectMany(ToSources));
    }

    private static IEnumerable<ProxyUpstreamHealthSource> ToSources(RuntimeRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return route.Upstreams.Select(upstream => ToSource(route, upstream));
    }

    private static ProxyUpstreamHealthSource ToSource(RuntimeRoute route, RuntimeUpstream upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        return new ProxyUpstreamHealthSource(UpstreamHealthStateSourceMapper.FromUpstream(upstream), CircuitBreakerStatusSourceMapper.FromUpstream(upstream), upstream.Scheme, upstream.Protocol, upstream.Weight, string.Equals(upstream.Scheme, "https", StringComparison.OrdinalIgnoreCase) && upstream.Tls.ValidateCertificate, string.Equals(upstream.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? upstream.EffectiveSniHost : null, route.HealthCheck.Enabled);
    }
}
