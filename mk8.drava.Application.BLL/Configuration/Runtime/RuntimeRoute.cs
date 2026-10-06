using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;

namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeRoute
{
    public RuntimeRoute(string Name, string Host, string PathPrefix, RuntimeRouteAction Action, string LoadBalancingPolicy, RuntimeHealthCheckOptions HealthCheck, IReadOnlyList<RuntimeUpstream> Upstreams, RuntimeHttpsRedirectPolicy HttpsRedirect, RuntimeCanonicalHostPolicy CanonicalHost, RuntimeHeaderPolicy HeaderPolicy, RuntimePathRewritePolicy PathRewrite, RuntimeRedirectPolicy Redirect, RuntimeStaticResponse StaticResponse, RuntimeMaintenancePolicy Maintenance, RuntimeCachePolicy Cache, RuntimeRouteResolvedOptions ResolvedOptions) : this(Name, Host, PathPrefix, Action, LoadBalancingPolicy, HealthCheck, Upstreams, HttpsRedirect, CanonicalHost, HeaderPolicy, PathRewrite, Redirect, StaticResponse, Maintenance, Cache, ResolvedOptions, SiteName: "", Retry: RuntimeRetryPolicy.Disabled)
    {
    }

    public RuntimeRoute(string Name, string Host, string PathPrefix, RuntimeRouteAction Action, string LoadBalancingPolicy, RuntimeHealthCheckOptions HealthCheck, IReadOnlyList<RuntimeUpstream> Upstreams, RuntimeHttpsRedirectPolicy HttpsRedirect, RuntimeCanonicalHostPolicy CanonicalHost, RuntimeHeaderPolicy HeaderPolicy, RuntimePathRewritePolicy PathRewrite, RuntimeRedirectPolicy Redirect, RuntimeStaticResponse StaticResponse, RuntimeMaintenancePolicy Maintenance, RuntimeCachePolicy Cache, RuntimeRouteResolvedOptions ResolvedOptions, string SiteName, RuntimeRetryPolicy Retry, UpstreamBalancingPolicy? Balancing = null)
    {
        RuntimeRouteFacts.Validate(Name, Host, PathPrefix, Action, LoadBalancingPolicy, SiteName);
        ArgumentNullException.ThrowIfNull(Upstreams);
        ArgumentNullException.ThrowIfNull(HealthCheck);
        ArgumentNullException.ThrowIfNull(HttpsRedirect);
        ArgumentNullException.ThrowIfNull(CanonicalHost);
        ArgumentNullException.ThrowIfNull(HeaderPolicy);
        ArgumentNullException.ThrowIfNull(PathRewrite);
        ArgumentNullException.ThrowIfNull(Redirect);
        ArgumentNullException.ThrowIfNull(StaticResponse);
        ArgumentNullException.ThrowIfNull(Maintenance);
        ArgumentNullException.ThrowIfNull(Cache);
        ArgumentNullException.ThrowIfNull(ResolvedOptions);
        ArgumentNullException.ThrowIfNull(Retry);
        this.Name = Name;
        this.Host = Host;
        this.PathPrefix = PathPrefix;
        this.Action = Action;
        this.LoadBalancingPolicy = LoadBalancingPolicy;
        this.HealthCheck = HealthCheck;
        this.Upstreams = RuntimeList.Copy(Upstreams);
        this.HttpsRedirect = HttpsRedirect;
        this.CanonicalHost = CanonicalHost;
        this.HeaderPolicy = HeaderPolicy;
        this.PathRewrite = PathRewrite;
        this.Redirect = Redirect;
        this.StaticResponse = StaticResponse;
        this.Maintenance = Maintenance;
        this.Cache = Cache;
        this.ResolvedOptions = ResolvedOptions;
        this.SiteName = SiteName;
        this.Retry = Retry;
        this.Balancing = Balancing;
    }

    public string Name { get; }
    public string Host { get; }
    public string PathPrefix { get; }
    public RuntimeRouteAction Action { get; }
    public string LoadBalancingPolicy { get; }
    public RuntimeHealthCheckOptions HealthCheck { get; }
    public IReadOnlyList<RuntimeUpstream> Upstreams { get; }
    public RuntimeHttpsRedirectPolicy HttpsRedirect { get; }
    public RuntimeCanonicalHostPolicy CanonicalHost { get; }
    public RuntimeHeaderPolicy HeaderPolicy { get; }
    public RuntimePathRewritePolicy PathRewrite { get; }
    public RuntimeRedirectPolicy Redirect { get; }
    public RuntimeStaticResponse StaticResponse { get; }
    public RuntimeMaintenancePolicy Maintenance { get; }
    public RuntimeCachePolicy Cache { get; }
    public RuntimeRouteResolvedOptions ResolvedOptions { get; }
    public string SiteName { get; }
    public RuntimeRetryPolicy Retry { get; }
    public UpstreamBalancingPolicy? Balancing { get; }

    public RuntimeRoute WithUpstreams(IReadOnlyList<RuntimeUpstream> upstreams)
    {
        return new RuntimeRoute(Name, Host, PathPrefix, Action, LoadBalancingPolicy, HealthCheck, upstreams, HttpsRedirect, CanonicalHost, HeaderPolicy, PathRewrite, Redirect, StaticResponse, Maintenance, Cache, ResolvedOptions, SiteName, Retry, Balancing);
    }
}
