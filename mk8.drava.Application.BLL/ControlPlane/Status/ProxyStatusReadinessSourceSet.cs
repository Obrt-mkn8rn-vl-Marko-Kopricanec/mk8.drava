using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyStatusReadinessSourceSet
{
    public ProxyStatusReadinessSourceSet(bool HasActiveConfiguration, int? ConfigGeneration, DateTimeOffset? ConfigurationLoadedAtUtc, bool? LastListenerReloadSucceeded, bool LastListenerReloadFailed, IReadOnlyList<ProxyConfiguredListenerSummarySource> ConfiguredListeners, IReadOnlyList<ProxyRuntimeListenerSummarySource> RuntimeListeners, IReadOnlyList<ProxyRouteSummarySource> Routes, ProxyCertificateSummarySource? Certificates, ProxyAcmeSummaryConfigurationSource? Acme, IReadOnlyList<ProxyUpstreamSummarySource> Upstreams, ProxyLimitConfigurationSummarySource? LimitConfiguration, ProxyLimitRuntimeSummarySource LimitRuntime, bool ClientHttp3Enabled, bool ClientHttp3Ready, ProxyLogSummarySource Log, ProxyShutdownSummarySource Shutdown)
    {
        ArgumentNullException.ThrowIfNull(ConfiguredListeners);
        ArgumentNullException.ThrowIfNull(RuntimeListeners);
        ArgumentNullException.ThrowIfNull(Routes);
        ArgumentNullException.ThrowIfNull(Upstreams);
        ProxyStatusFacts.RequireOptionalNonNegative(ConfigGeneration, nameof(ConfigGeneration));
        this.HasActiveConfiguration = HasActiveConfiguration;
        this.ConfigGeneration = ConfigGeneration;
        this.ConfigurationLoadedAtUtc = ConfigurationLoadedAtUtc;
        this.LastListenerReloadSucceeded = LastListenerReloadSucceeded;
        this.LastListenerReloadFailed = LastListenerReloadFailed;
        this.ConfiguredListeners = ProxyStatusList.Copy(ConfiguredListeners);
        this.RuntimeListeners = ProxyStatusList.Copy(RuntimeListeners);
        this.Routes = ProxyStatusList.Copy(Routes);
        this.Certificates = Certificates;
        this.Acme = Acme;
        this.Upstreams = ProxyStatusList.Copy(Upstreams);
        this.LimitConfiguration = LimitConfiguration;
        this.LimitRuntime = LimitRuntime;
        this.ClientHttp3Enabled = ClientHttp3Enabled;
        this.ClientHttp3Ready = ClientHttp3Ready;
        this.Log = Log;
        this.Shutdown = Shutdown;
    }

    public bool HasActiveConfiguration { get; }
    public int? ConfigGeneration { get; }
    public DateTimeOffset? ConfigurationLoadedAtUtc { get; }
    public bool? LastListenerReloadSucceeded { get; }
    public bool LastListenerReloadFailed { get; }
    public IReadOnlyList<ProxyConfiguredListenerSummarySource> ConfiguredListeners { get; }
    public IReadOnlyList<ProxyRuntimeListenerSummarySource> RuntimeListeners { get; }
    public IReadOnlyList<ProxyRouteSummarySource> Routes { get; }
    public ProxyCertificateSummarySource? Certificates { get; }
    public ProxyAcmeSummaryConfigurationSource? Acme { get; }
    public IReadOnlyList<ProxyUpstreamSummarySource> Upstreams { get; }
    public ProxyLimitConfigurationSummarySource? LimitConfiguration { get; }
    public ProxyLimitRuntimeSummarySource LimitRuntime { get; }
    public bool ClientHttp3Enabled { get; }
    public bool ClientHttp3Ready { get; }
    public ProxyLogSummarySource Log { get; }
    public ProxyShutdownSummarySource Shutdown { get; }
}
