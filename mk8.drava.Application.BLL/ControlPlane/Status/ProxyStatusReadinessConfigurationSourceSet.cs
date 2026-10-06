using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyStatusReadinessConfigurationSourceSet
{
    public ProxyStatusReadinessConfigurationSourceSet(bool HasActiveConfiguration, int? ConfigGeneration, DateTimeOffset? ConfigurationLoadedAtUtc, IReadOnlyList<ProxyConfiguredListenerSummarySource> ConfiguredListeners, IReadOnlyList<ProxyRouteSummarySource> Routes, ProxyCertificateSummarySource? Certificates, ProxyAcmeSummaryConfigurationSource? Acme, ProxyLimitConfigurationSummarySource? LimitConfiguration)
    {
        ArgumentNullException.ThrowIfNull(ConfiguredListeners);
        ArgumentNullException.ThrowIfNull(Routes);
        ProxyStatusFacts.RequireOptionalNonNegative(ConfigGeneration, nameof(ConfigGeneration));
        this.HasActiveConfiguration = HasActiveConfiguration;
        this.ConfigGeneration = ConfigGeneration;
        this.ConfigurationLoadedAtUtc = ConfigurationLoadedAtUtc;
        this.ConfiguredListeners = ProxyStatusList.Copy(ConfiguredListeners);
        this.Routes = ProxyStatusList.Copy(Routes);
        this.Certificates = Certificates;
        this.Acme = Acme;
        this.LimitConfiguration = LimitConfiguration;
    }

    public static ProxyStatusReadinessConfigurationSourceSet Missing { get; } = new(HasActiveConfiguration: false, ConfigGeneration: null, ConfigurationLoadedAtUtc: null, ConfiguredListeners: [], Routes: [], Certificates: null, Acme: null, LimitConfiguration: null);
    public bool HasActiveConfiguration { get; }
    public int? ConfigGeneration { get; }
    public DateTimeOffset? ConfigurationLoadedAtUtc { get; }
    public IReadOnlyList<ProxyConfiguredListenerSummarySource> ConfiguredListeners { get; }
    public IReadOnlyList<ProxyRouteSummarySource> Routes { get; }
    public ProxyCertificateSummarySource? Certificates { get; }
    public ProxyAcmeSummaryConfigurationSource? Acme { get; }
    public ProxyLimitConfigurationSummarySource? LimitConfiguration { get; }
}
