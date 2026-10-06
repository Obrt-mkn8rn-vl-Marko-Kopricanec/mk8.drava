namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class SiteOptions
{
    public string Name { get; init; } = "";
    public System.Collections.ObjectModel.Collection<ListenerOptions> Listeners { get; init; } = [];
    public string Host { get; init; } = "";
    public string PathPrefix { get; init; } = "/";
    public string LoadBalancingPolicy { get; init; } = "round-robin";
    public HealthCheckOptions HealthCheck { get; init; } = new();
    public System.Collections.ObjectModel.Collection<UpstreamOptions> Upstreams { get; init; } = [];
    public ProxyHttpsRedirectOptions HttpsRedirect { get; init; } = new();
    public ProxyCanonicalHostOptions CanonicalHost { get; init; } = new();
    public ProxyHeaderPolicyOptions HeaderPolicy { get; init; } = new();
    public ProxyMaintenanceOptions Maintenance { get; init; } = new();
    public ProxyCachePolicyOptions Cache { get; init; } = new();
    public ProxyRetryPolicyOptions Retry { get; init; } = new();
    public ProxyRouteOverrideOptions Overrides { get; init; } = new();
    public System.Collections.ObjectModel.Collection<ProxyRouteOptions> Routes { get; init; } = [];
}
