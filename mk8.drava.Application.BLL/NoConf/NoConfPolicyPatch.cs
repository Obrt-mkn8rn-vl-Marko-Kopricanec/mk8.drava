using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;

namespace Mk8.Drava.Application.BLL.NoConf;

public sealed record NoConfPolicyPatch
{
    public string? Host { get; init; }
    public string? PathPrefix { get; init; }
    public string? Action { get; init; }
    public ProxyRedirectOptions? Redirect { get; init; }
    public ProxyStaticResponseOptions? StaticResponse { get; init; }
    public BalancingAlgorithm? Algorithm { get; init; }
    public string? AffinityHeader { get; init; }
    public string? PreferredZone { get; init; }
    public bool? RequireLocalZone { get; init; }
    public ProxyHeaderPolicyOptions? Headers { get; init; }
    public ProxyPathRewriteOptions? Rewrite { get; init; }
    public ProxyCachePolicyOptions? Cache { get; init; }
    public ProxyRetryPolicyOptions? Retry { get; init; }
    public ProxyMaintenanceOptions? Maintenance { get; init; }
    public ProxyHttpsRedirectOptions? HttpsRedirect { get; init; }
    public ProxyCanonicalHostOptions? CanonicalHost { get; init; }
    public ProxyRouteOverrideOptions? Limits { get; init; }
    public UpstreamTlsOptions? UpstreamTls { get; init; }
    public ProxyCircuitBreakerOptions? CircuitBreaker { get; init; }
}
