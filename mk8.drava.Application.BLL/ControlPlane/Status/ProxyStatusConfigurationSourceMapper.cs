using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyStatusConfigurationSourceMapper
{
    public static ProxyStatusConfigurationSourceSet FromSources(int version, DateTimeOffset loadedAtUtc, IEnumerable<RuntimeListener> listeners, IEnumerable<RuntimeRoute> routes, IEnumerable<RuntimeCertificate> certificates, RuntimeAcmeOptions acme, RuntimeLimits limits, IEnumerable<ProxyUpstreamHealthSource> upstreamHealthSources)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(acme);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(upstreamHealthSources);
        var listenerSources = ProxyStatusList.Copy(listeners);
        var routeSources = ProxyStatusList.Copy(routes);
        return new ProxyStatusConfigurationSourceSet(ProxyStatusConfigurationSummaryMapper.FromCounts(version, loadedAtUtc, listenerSources.Count, routeSources.Count), upstreamHealthSources, ProxyHttp3SupportConfigurationSourceMapper.FromSources(listenerSources, routeSources), ProxyStatusReadinessConfigurationSourceMapper.FromSources(version, loadedAtUtc, listenerSources, routeSources, certificates, acme, limits));
    }
}
