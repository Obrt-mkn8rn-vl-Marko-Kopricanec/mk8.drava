namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyMetricsSnapshot(ProxyClientConnectionMetricsSnapshot ClientConnections, ProxyTrafficMetricsSnapshot Traffic, ProxyUpstreamForwardingMetricsSnapshot UpstreamForwarding, ProxyClientFailureMetricsSnapshot ClientFailures, ProxyRejectionMetricsSnapshot Rejections, ProxyUpstreamFailureMetricsSnapshot UpstreamFailureReasons, ProxyGeneratedResponseMetricsSnapshot GeneratedResponses, ProxyTlsMetricsSnapshot Tls, ProxyUpstreamPoolMetricsSnapshot UpstreamPool, ProxyUpgradeMetricsSnapshot Upgrades, ProxyTunnelMetricsSnapshot Tunnels, ProxyUpstreamSelectionMetricsSnapshot UpstreamSelections, ProxyHealthMetricsSnapshot Health, ProxyDiagnosticsMetricsSnapshot Diagnostics, ProxyRequestClassificationMetricsSnapshot RequestClassifications, ProxyConfigReloadMetricsSnapshot ConfigReloads, ProxyAdminAuthMetricsSnapshot AdminAuth, ProxyAcmeRenewalMetricsSnapshot AcmeRenewals, ProxyResilienceMetricsSnapshot Resilience, ProxyListenerMetricsSnapshot Listeners, ProxyHttp2MetricsSnapshot Http2, ProxyUpstreamHttp2MetricsSnapshot UpstreamHttp2, ProxyUpstreamHttp3MetricsSnapshot UpstreamHttp3, ProxyHttp3MetricsSnapshot Http3, ProxyConfigLintMetricsSnapshot ConfigLint, ProxyRouteDiagnosticsMetricsSnapshot RouteDiagnostics)
{
    public ProxyClientConnectionMetricsSnapshot ClientConnections { get; } = ClientConnections ?? throw new ArgumentNullException(nameof(ClientConnections));
    public ProxyTrafficMetricsSnapshot Traffic { get; } = Traffic ?? throw new ArgumentNullException(nameof(Traffic));
    public ProxyClientFailureMetricsSnapshot ClientFailures { get; } = ClientFailures ?? throw new ArgumentNullException(nameof(ClientFailures));
    public ProxyUpstreamForwardingMetricsSnapshot UpstreamForwarding { get; } = UpstreamForwarding ?? throw new ArgumentNullException(nameof(UpstreamForwarding));
    public ProxyUpstreamSelectionMetricsSnapshot UpstreamSelections { get; } = UpstreamSelections ?? throw new ArgumentNullException(nameof(UpstreamSelections));
    public ProxyHttp3MetricsSnapshot Http3 { get; } = Http3 ?? throw new ArgumentNullException(nameof(Http3));
    public ProxyUpstreamHttp3MetricsSnapshot UpstreamHttp3 { get; } = UpstreamHttp3 ?? throw new ArgumentNullException(nameof(UpstreamHttp3));
    public ProxyUpstreamHttp2MetricsSnapshot UpstreamHttp2 { get; } = UpstreamHttp2 ?? throw new ArgumentNullException(nameof(UpstreamHttp2));
    public ProxyHttp2MetricsSnapshot Http2 { get; } = Http2 ?? throw new ArgumentNullException(nameof(Http2));
    public ProxyListenerMetricsSnapshot Listeners { get; } = Listeners ?? throw new ArgumentNullException(nameof(Listeners));
    public ProxyUpstreamPoolMetricsSnapshot UpstreamPool { get; } = UpstreamPool ?? throw new ArgumentNullException(nameof(UpstreamPool));
    public ProxyUpgradeMetricsSnapshot Upgrades { get; } = Upgrades ?? throw new ArgumentNullException(nameof(Upgrades));
    public ProxyHealthMetricsSnapshot Health { get; } = Health ?? throw new ArgumentNullException(nameof(Health));
    public ProxyRejectionMetricsSnapshot Rejections { get; } = Rejections ?? throw new ArgumentNullException(nameof(Rejections));
    public ProxyUpstreamFailureMetricsSnapshot UpstreamFailureReasons { get; } = UpstreamFailureReasons ?? throw new ArgumentNullException(nameof(UpstreamFailureReasons));
    public ProxyGeneratedResponseMetricsSnapshot GeneratedResponses { get; } = GeneratedResponses ?? throw new ArgumentNullException(nameof(GeneratedResponses));
    public ProxyDiagnosticsMetricsSnapshot Diagnostics { get; } = Diagnostics ?? throw new ArgumentNullException(nameof(Diagnostics));
    public ProxyRequestClassificationMetricsSnapshot RequestClassifications { get; } = RequestClassifications ?? throw new ArgumentNullException(nameof(RequestClassifications));
    public ProxyConfigReloadMetricsSnapshot ConfigReloads { get; } = ConfigReloads ?? throw new ArgumentNullException(nameof(ConfigReloads));
    public ProxyConfigLintMetricsSnapshot ConfigLint { get; } = ConfigLint ?? throw new ArgumentNullException(nameof(ConfigLint));
    public ProxyRouteDiagnosticsMetricsSnapshot RouteDiagnostics { get; } = RouteDiagnostics ?? throw new ArgumentNullException(nameof(RouteDiagnostics));
    public ProxyAdminAuthMetricsSnapshot AdminAuth { get; } = AdminAuth ?? throw new ArgumentNullException(nameof(AdminAuth));
    public ProxyAcmeRenewalMetricsSnapshot AcmeRenewals { get; } = AcmeRenewals ?? throw new ArgumentNullException(nameof(AcmeRenewals));
    public ProxyResilienceMetricsSnapshot Resilience { get; } = Resilience ?? throw new ArgumentNullException(nameof(Resilience));
    public ProxyTunnelMetricsSnapshot Tunnels { get; } = Tunnels ?? throw new ArgumentNullException(nameof(Tunnels));
    public ProxyTlsMetricsSnapshot Tls { get; } = Tls ?? throw new ArgumentNullException(nameof(Tls));
}
