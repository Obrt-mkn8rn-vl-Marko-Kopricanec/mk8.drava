using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public sealed record ProxyConfigurationProjection
{
    public ProxyConfigurationProjection(int Version, DateTimeOffset LoadedAtUtc, string SourceDirectory, IReadOnlyList<string> SourceFiles, ProxyConfigurationDiscovery Discovery, RuntimeAdminSecurityProjection AdminSecurity, RuntimeAcmeProjection Acme, RuntimeTimeoutsProjection Timeouts, RuntimeConnectionLimitsProjection ConnectionLimits, RuntimeObservabilityProjection Observability, RuntimeLimitsProjection Limits, RuntimeForwardedHeadersProjection ForwardedHeaders, RuntimeMetricsProjection Metrics, RuntimeHttp3SupportProjection Http3, IReadOnlyList<RuntimeCertificateProjection> Certificates, IReadOnlyList<RuntimeListenerProjection> Listeners, IReadOnlyList<RuntimeRouteProjection> Routes)
    {
        ArgumentNullException.ThrowIfNull(SourceDirectory);
        ArgumentNullException.ThrowIfNull(Discovery);
        ArgumentNullException.ThrowIfNull(AdminSecurity);
        ArgumentNullException.ThrowIfNull(Acme);
        ArgumentNullException.ThrowIfNull(Timeouts);
        ArgumentNullException.ThrowIfNull(ConnectionLimits);
        ArgumentNullException.ThrowIfNull(Observability);
        ArgumentNullException.ThrowIfNull(Limits);
        ArgumentNullException.ThrowIfNull(ForwardedHeaders);
        ArgumentNullException.ThrowIfNull(Metrics);
        ArgumentNullException.ThrowIfNull(Http3);
        this.Version = Version;
        this.LoadedAtUtc = LoadedAtUtc;
        this.SourceDirectory = SourceDirectory;
        this.SourceFiles = ConfigurationManagementList.Copy(SourceFiles);
        this.Discovery = Discovery;
        this.AdminSecurity = AdminSecurity;
        this.Acme = Acme;
        this.Timeouts = Timeouts;
        this.ConnectionLimits = ConnectionLimits;
        this.Observability = Observability;
        this.Limits = Limits;
        this.ForwardedHeaders = ForwardedHeaders;
        this.Metrics = Metrics;
        this.Http3 = Http3;
        this.Certificates = ConfigurationManagementList.Copy(Certificates);
        this.Listeners = ConfigurationManagementList.Copy(Listeners);
        this.Routes = ConfigurationManagementList.Copy(Routes);
    }

    public int Version { get; }
    public DateTimeOffset LoadedAtUtc { get; }
    public string SourceDirectory { get; }
    public IReadOnlyList<string> SourceFiles { get; }
    public ProxyConfigurationDiscovery Discovery { get; }
    public RuntimeAdminSecurityProjection AdminSecurity { get; }
    public RuntimeAcmeProjection Acme { get; }
    public RuntimeTimeoutsProjection Timeouts { get; }
    public RuntimeConnectionLimitsProjection ConnectionLimits { get; }
    public RuntimeObservabilityProjection Observability { get; }
    public RuntimeLimitsProjection Limits { get; }
    public RuntimeForwardedHeadersProjection ForwardedHeaders { get; }
    public RuntimeMetricsProjection Metrics { get; }
    public RuntimeHttp3SupportProjection Http3 { get; }
    public IReadOnlyList<RuntimeCertificateProjection> Certificates { get; }
    public IReadOnlyList<RuntimeListenerProjection> Listeners { get; }
    public IReadOnlyList<RuntimeRouteProjection> Routes { get; }
}
