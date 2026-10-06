namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record ProxyConfigurationSnapshot
{
    public ProxyConfigurationSnapshot(int Version, DateTimeOffset LoadedAtUtc, string SourceDirectory, IReadOnlyList<string> SourceFiles, ProxyConfigurationDiscovery Discovery, RuntimeAdminSecurityOptions AdminSecurity, RuntimeAcmeOptions Acme, RuntimeTimeouts Timeouts, RuntimeConnectionLimits ConnectionLimits, RuntimeObservabilityOptions Observability, RuntimeLimits Limits, RuntimeForwardedHeadersOptions ForwardedHeaders, IReadOnlyDictionary<string, RuntimeCertificate> Certificates, IReadOnlyList<RuntimeListener> Listeners, IReadOnlyList<RuntimeRoute> Routes) : this(Version, LoadedAtUtc, SourceDirectory, SourceFiles, Discovery, AdminSecurity, Acme, Timeouts, ConnectionLimits, Observability, Limits, ForwardedHeaders, Certificates, Listeners, Routes, RuntimeMetricsOptions.Default)
    {
    }

    public ProxyConfigurationSnapshot(int Version, DateTimeOffset LoadedAtUtc, string SourceDirectory, IReadOnlyList<string> SourceFiles, ProxyConfigurationDiscovery Discovery, RuntimeAdminSecurityOptions AdminSecurity, RuntimeAcmeOptions Acme, RuntimeTimeouts Timeouts, RuntimeConnectionLimits ConnectionLimits, RuntimeObservabilityOptions Observability, RuntimeLimits Limits, RuntimeForwardedHeadersOptions ForwardedHeaders, IReadOnlyDictionary<string, RuntimeCertificate> Certificates, IReadOnlyList<RuntimeListener> Listeners, IReadOnlyList<RuntimeRoute> Routes, RuntimeMetricsOptions Metrics)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Version);
        this.Version = Version;
        this.LoadedAtUtc = LoadedAtUtc;
        this.SourceDirectory = SourceDirectory;
        this.SourceFiles = RuntimeList.Copy(SourceFiles);
        this.Discovery = Discovery;
        this.AdminSecurity = AdminSecurity;
        this.Acme = Acme;
        this.Timeouts = Timeouts;
        this.ConnectionLimits = ConnectionLimits;
        this.Observability = Observability;
        this.Limits = Limits;
        this.ForwardedHeaders = ForwardedHeaders;
        this.Certificates = RuntimeList.CopyDictionary(Certificates, StringComparer.OrdinalIgnoreCase);
        this.Listeners = RuntimeList.Copy(Listeners);
        this.Routes = RuntimeList.Copy(Routes);
        this.Metrics = Metrics;
    }

    public int Version { get; }
    public DateTimeOffset LoadedAtUtc { get; }
    public string SourceDirectory { get; }
    public IReadOnlyList<string> SourceFiles { get; }
    public ProxyConfigurationDiscovery Discovery { get; }
    public RuntimeAdminSecurityOptions AdminSecurity { get; }
    public RuntimeAcmeOptions Acme { get; }
    public RuntimeTimeouts Timeouts { get; }
    public RuntimeConnectionLimits ConnectionLimits { get; }
    public RuntimeObservabilityOptions Observability { get; }
    public RuntimeLimits Limits { get; }
    public RuntimeForwardedHeadersOptions ForwardedHeaders { get; }
    public IReadOnlyDictionary<string, RuntimeCertificate> Certificates { get; }
    public IReadOnlyList<RuntimeListener> Listeners { get; }
    public IReadOnlyList<RuntimeRoute> Routes { get; }
    public RuntimeMetricsOptions Metrics { get; }

    public ProxyConfigurationSnapshot WithCertificates(IReadOnlyDictionary<string, RuntimeCertificate> certificates)
    {
        return new ProxyConfigurationSnapshot(Version, LoadedAtUtc, SourceDirectory, SourceFiles, Discovery, AdminSecurity, Acme, Timeouts, ConnectionLimits, Observability, Limits, ForwardedHeaders, certificates, Listeners, Routes, Metrics);
    }

    public ProxyConfigurationSnapshot WithListenersAndRoutes(IReadOnlyList<RuntimeListener> listeners, IReadOnlyList<RuntimeRoute> routes)
    {
        return new ProxyConfigurationSnapshot(Version, LoadedAtUtc, SourceDirectory, SourceFiles, Discovery, AdminSecurity, Acme, Timeouts, ConnectionLimits, Observability, Limits, ForwardedHeaders, Certificates, listeners, routes, Metrics);
    }
}
