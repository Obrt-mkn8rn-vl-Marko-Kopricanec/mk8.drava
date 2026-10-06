namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeRouteProjection
{
    public RuntimeRouteProjection(string Name, string Host, string PathPrefix, RuntimeRouteAction Action, string LoadBalancingPolicy, RuntimeHealthCheckProjection HealthCheck, IReadOnlyList<RuntimeUpstreamProjection> Upstreams, RuntimeHttpsRedirectProjection HttpsRedirect, RuntimeCanonicalHostProjection CanonicalHost, RuntimeHeaderPolicyProjection HeaderPolicy, RuntimePathRewriteProjection PathRewrite, RuntimeRedirectProjection Redirect, RuntimeStaticResponseProjection StaticResponse, RuntimeMaintenanceProjection Maintenance, RuntimeCacheProjection Cache, RuntimeRouteResolvedOptionsProjection ResolvedOptions, string SiteName, RuntimeRetryProjection Retry)
    {
        RuntimeRouteFacts.Validate(Name, Host, PathPrefix, Action, LoadBalancingPolicy, SiteName);
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
    }

    public string Name { get; }
    public string Host { get; }
    public string PathPrefix { get; }
    public RuntimeRouteAction Action { get; }
    public string LoadBalancingPolicy { get; }
    public RuntimeHealthCheckProjection HealthCheck { get; }
    public IReadOnlyList<RuntimeUpstreamProjection> Upstreams { get; }
    public RuntimeHttpsRedirectProjection HttpsRedirect { get; }
    public RuntimeCanonicalHostProjection CanonicalHost { get; }
    public RuntimeHeaderPolicyProjection HeaderPolicy { get; }
    public RuntimePathRewriteProjection PathRewrite { get; }
    public RuntimeRedirectProjection Redirect { get; }
    public RuntimeStaticResponseProjection StaticResponse { get; }
    public RuntimeMaintenanceProjection Maintenance { get; }
    public RuntimeCacheProjection Cache { get; }
    public RuntimeRouteResolvedOptionsProjection ResolvedOptions { get; }
    public string SiteName { get; }
    public RuntimeRetryProjection Retry { get; }
}
