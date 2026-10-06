using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyStatusReadinessConfigurationSourceMapper
{
    public static ProxyStatusReadinessConfigurationSourceSet FromSources(int version, DateTimeOffset loadedAtUtc, IEnumerable<RuntimeListener> listeners, IEnumerable<RuntimeRoute> routes, IEnumerable<RuntimeCertificate> certificates, RuntimeAcmeOptions acme, RuntimeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(acme);
        ArgumentNullException.ThrowIfNull(limits);
        var listenerSources = ProxyStatusList.Copy(listeners);
        var routeSources = ProxyStatusList.Copy(routes);
        return new ProxyStatusReadinessConfigurationSourceSet(true, version, loadedAtUtc, ProxyConfiguredListenerSummarySourceMapper.FromListeners(listenerSources), ProxyRouteSummarySourceMapper.FromRoutes(routeSources), ProxyCertificateSummarySourceMapper.FromSources(listenerSources, certificates), ProxyAcmeSummaryConfigurationSourceMapper.FromSource(acme), ProxyLimitConfigurationSummarySourceMapper.FromSource(limits));
    }
}
