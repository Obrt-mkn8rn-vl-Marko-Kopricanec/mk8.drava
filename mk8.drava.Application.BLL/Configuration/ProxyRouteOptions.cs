namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyRouteOptions
{
    public string Name { get; init; } = "";
    public string SiteName { get; init; } = "";
    public string Host { get; init; } = "*";
    public string PathPrefix { get; init; } = "/";
    public string Action { get; init; } = "proxy";
    public string LoadBalancingPolicy { get; init; } = "round-robin";
    public HealthCheckOptions HealthCheck { get; init; } = new();
    public IList<UpstreamOptions> Upstreams { get; init; } = [];
    public ProxyHttpsRedirectOptions HttpsRedirect { get; init; } = new();
    public ProxyCanonicalHostOptions CanonicalHost { get; init; } = new();
    public ProxyHeaderPolicyOptions HeaderPolicy { get; init; } = new();
    public ProxyPathRewriteOptions PathRewrite { get; init; } = new();
    public ProxyRedirectOptions Redirect { get; init; } = new();
    public ProxyStaticResponseOptions StaticResponse { get; init; } = new();
    public ProxyMaintenanceOptions Maintenance { get; init; } = new();
    public ProxyCachePolicyOptions Cache { get; init; } = new();
    public ProxyRetryPolicyOptions Retry { get; init; } = new();
    public ProxyRouteOverrideOptions Overrides { get; init; } = new();
}
