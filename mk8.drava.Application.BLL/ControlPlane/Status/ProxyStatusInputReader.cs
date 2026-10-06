using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Observability;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed class ProxyStatusInputReader : IProxyStatusInputReader
{
    private readonly IProxyStatusRuntimeStateSource _runtimeSource;
    private readonly IProxyStatusMetricsSource _metricsSource;
    private readonly IProxyStatusConfigurationSource _configurationSource;
    private readonly IProxyStatusUpstreamHealthReader _upstreamReader;
    private readonly IProxyConfigLintOperations _lintOperations;
    private readonly IProxyLogPersistenceStore _logPersistenceStore;
    private readonly IProxyCacheStatusReader _cacheStatusReader;
    private readonly IProxyAcmeCertificateLifecycleStatusSource _acmeStatusSource;
    private readonly IProxyStatusRuntimePreflightSource _preflightSource;
    private readonly IRuntimeHttp3PlatformSupportSource _http3PlatformSupportSource;
    private readonly TimeProvider _timeProvider;
    public ProxyStatusInputReader(IProxyStatusRuntimeStateSource runtimeSource, IProxyStatusMetricsSource metricsSource, IProxyStatusConfigurationSource configurationSource, IProxyStatusUpstreamHealthReader upstreamReader, IProxyConfigLintOperations lintOperations, IProxyLogPersistenceStore logPersistenceStore, IProxyCacheStatusReader cacheStatusReader, IProxyAcmeCertificateLifecycleStatusSource acmeStatusSource, IProxyStatusRuntimePreflightSource preflightSource, IRuntimeHttp3PlatformSupportSource http3PlatformSupportSource, TimeProvider timeProvider)
    {
        _runtimeSource = runtimeSource;
        _metricsSource = metricsSource;
        _configurationSource = configurationSource;
        _upstreamReader = upstreamReader;
        _lintOperations = lintOperations;
        _logPersistenceStore = logPersistenceStore;
        _cacheStatusReader = cacheStatusReader;
        _acmeStatusSource = acmeStatusSource;
        _preflightSource = preflightSource;
        _http3PlatformSupportSource = http3PlatformSupportSource;
        _timeProvider = timeProvider;
    }

    public ProxyStatusInput Read()
    {
        var runtimeSummary = _runtimeSource.ReadRuntimeSummary();
        var configurationResult = _configurationSource.ReadConfiguration();
        var configuration = configurationResult is ProxyStatusConfigurationReadResult.AvailableResult available ? available.Configuration : null;
        var readinessConfiguration = configuration?.ReadinessConfiguration ?? ProxyStatusReadinessConfigurationSourceSet.Missing;
        var upstreams = _upstreamReader.ReadUpstreams();
        var metrics = _metricsSource.ReadMetrics();
        var http3 = Http3RuntimeSupport.ProjectRuntime(configuration?.Http3Configuration ?? Http3SupportConfigurationSource.Empty, _http3PlatformSupportSource.Read(), Http3SupportSourceMapper.FromListenerStatuses(runtimeSummary.Listeners));
        var logPersistence = _logPersistenceStore.GetStatus();
        var runtimePreflight = _preflightSource.ReadRuntimePreflight();
        var cacheStatus = _cacheStatusReader.GetStatus();
        var acmeStatuses = _acmeStatusSource.GetLifecycleStatuses();
        var observedAtUtc = _timeProvider.GetUtcNow();
        var readiness = ProxyStatusReadinessInputMapper.FromSources(ProxyStatusReadinessSourceMapper.FromSources(readinessConfiguration, runtimeSummary, metrics, upstreams, http3, logPersistence), cacheStatus, acmeStatuses, runtimePreflight, observedAtUtc);
        return new ProxyStatusInput(runtimeSummary, configuration is null ? null : configuration.ConfigurationSummary, metrics, upstreams, http3, logPersistence, cacheStatus, acmeStatuses, runtimePreflight, observedAtUtc, readiness, _lintOperations.LastActiveStatus);
    }
}
