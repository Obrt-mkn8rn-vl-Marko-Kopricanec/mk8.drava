namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyOperationalOptions
{
    public ProxyAdminOptions Admin { get; init; } = new();
    public ProxyAcmeOptions Acme { get; init; } = new();
    public ProxyMetricsOptions Metrics { get; init; } = new();
    public ProxyTimeoutOptions Timeouts { get; init; } = new();
    public ProxyConnectionOptions Connections { get; init; } = new();
    public ProxyObservabilityOptions Observability { get; init; } = new();
    public ProxyLimitsOptions Limits { get; init; } = new();
    public ProxyForwardedHeadersOptions ForwardedHeaders { get; init; } = new();
    public System.Collections.ObjectModel.Collection<CertificateOptions> Certificates { get; init; } = [];
}
