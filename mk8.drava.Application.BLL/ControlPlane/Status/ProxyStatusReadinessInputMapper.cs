using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyStatusReadinessInputMapper
{
    public static ProxyStatusReadinessInput FromSources(ProxyStatusReadinessSourceSet sources, ProxyCacheStatus? cacheStatus, IReadOnlyList<AcmeCertificateLifecycleStatus> acmeStatuses, ProxyRuntimePreflightStatus runtimePreflight, DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return new ProxyStatusReadinessInput(sources.HasActiveConfiguration, sources.ConfigGeneration, sources.ConfigurationLoadedAtUtc, sources.LastListenerReloadSucceeded, sources.LastListenerReloadFailed, sources.ConfiguredListeners, sources.RuntimeListeners, sources.Routes, sources.Certificates, sources.Acme, sources.Upstreams, sources.LimitConfiguration, sources.LimitRuntime, sources.ClientHttp3Enabled, sources.ClientHttp3Ready, sources.Log, sources.Shutdown, cacheStatus, acmeStatuses, runtimePreflight, observedAtUtc);
    }
}
