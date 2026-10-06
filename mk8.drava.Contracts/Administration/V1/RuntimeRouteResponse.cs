namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeRouteResponse
{
    public RuntimeRouteResponse(string name, string host, string pathPrefix, RuntimeRouteActionResponse action, string loadBalancingPolicy, RuntimeHealthCheckResponse healthCheck, IReadOnlyList<RuntimeUpstreamResponse> upstreams, RuntimeHttpsRedirectResponse httpsRedirect, RuntimeCanonicalHostResponse canonicalHost, RuntimeHeaderPolicyResponse headerPolicy, RuntimePathRewriteResponse pathRewrite, RuntimeRedirectResponse redirect, RuntimeStaticResponseResponse staticResponse, RuntimeMaintenanceResponse maintenance, RuntimeCachePolicyResponse cache, RuntimeRouteResolvedOptionsResponse resolvedOptions, string siteName, RuntimeRetryPolicyResponse retry)
    {
        ArgumentNullException.ThrowIfNull(healthCheck);
        ArgumentNullException.ThrowIfNull(httpsRedirect);
        ArgumentNullException.ThrowIfNull(canonicalHost);
        ArgumentNullException.ThrowIfNull(headerPolicy);
        ArgumentNullException.ThrowIfNull(pathRewrite);
        ArgumentNullException.ThrowIfNull(redirect);
        ArgumentNullException.ThrowIfNull(staticResponse);
        ArgumentNullException.ThrowIfNull(maintenance);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(resolvedOptions);
        ArgumentNullException.ThrowIfNull(retry);
        Name = name;
        Host = host;
        PathPrefix = pathPrefix;
        Action = action;
        LoadBalancingPolicy = loadBalancingPolicy;
        HealthCheck = healthCheck;
        Upstreams = ApiResponseList.Copy(upstreams);
        HttpsRedirect = httpsRedirect;
        CanonicalHost = canonicalHost;
        HeaderPolicy = headerPolicy;
        PathRewrite = pathRewrite;
        Redirect = redirect;
        StaticResponse = staticResponse;
        Maintenance = maintenance;
        Cache = cache;
        ResolvedOptions = resolvedOptions;
        SiteName = siteName;
        Retry = retry;
    }

    public string Name { get; }
    public string Host { get; }
    public string PathPrefix { get; }
    public RuntimeRouteActionResponse Action { get; }
    public string LoadBalancingPolicy { get; }
    public RuntimeHealthCheckResponse HealthCheck { get; }
    public IReadOnlyList<RuntimeUpstreamResponse> Upstreams { get; }
    public RuntimeHttpsRedirectResponse HttpsRedirect { get; }
    public RuntimeCanonicalHostResponse CanonicalHost { get; }
    public RuntimeHeaderPolicyResponse HeaderPolicy { get; }
    public RuntimePathRewriteResponse PathRewrite { get; }
    public RuntimeRedirectResponse Redirect { get; }
    public RuntimeStaticResponseResponse StaticResponse { get; }
    public RuntimeMaintenanceResponse Maintenance { get; }
    public RuntimeCachePolicyResponse Cache { get; }
    public RuntimeRouteResolvedOptionsResponse ResolvedOptions { get; }
    public string SiteName { get; }
    public RuntimeRetryPolicyResponse Retry { get; }
}
