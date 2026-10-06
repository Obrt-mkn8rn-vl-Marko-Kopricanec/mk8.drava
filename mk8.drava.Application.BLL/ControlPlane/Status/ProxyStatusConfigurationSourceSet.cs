using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyStatusConfigurationSourceSet
{
    public ProxyStatusConfigurationSourceSet(ProxyStatusConfigurationSummary ConfigurationSummary, IEnumerable<ProxyUpstreamHealthSource> UpstreamHealthSources, Http3SupportConfigurationSource Http3Configuration, ProxyStatusReadinessConfigurationSourceSet ReadinessConfiguration)
    {
        ArgumentNullException.ThrowIfNull(ConfigurationSummary);
        ArgumentNullException.ThrowIfNull(UpstreamHealthSources);
        ArgumentNullException.ThrowIfNull(Http3Configuration);
        ArgumentNullException.ThrowIfNull(ReadinessConfiguration);
        this.ConfigurationSummary = ConfigurationSummary;
        this.UpstreamHealthSources = ProxyStatusList.Copy(UpstreamHealthSources);
        this.Http3Configuration = Http3Configuration;
        this.ReadinessConfiguration = ReadinessConfiguration;
    }

    public ProxyStatusConfigurationSummary ConfigurationSummary { get; }
    public IReadOnlyList<ProxyUpstreamHealthSource> UpstreamHealthSources { get; }
    public Http3SupportConfigurationSource Http3Configuration { get; }
    public ProxyStatusReadinessConfigurationSourceSet ReadinessConfiguration { get; }
}
