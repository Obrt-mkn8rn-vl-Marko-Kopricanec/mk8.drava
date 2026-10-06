using BusinessRuntimeHealthCheckProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHealthCheckProjection;
using BusinessRuntimeRouteAction = Mk8.Drava.Application.BLL.Configuration.RuntimeRouteAction;
using BusinessRuntimeRouteProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeRouteProjection;
using BusinessRuntimeRouteResolvedOptionsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeRouteResolvedOptionsProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeRouteResponseMapper
{
    public static IReadOnlyList<RuntimeRouteResponse> FromRoutes(IEnumerable<BusinessRuntimeRouteProjection> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        return ApiResponseList.Copy(routes.Select(FromRoute));
    }

    private static RuntimeRouteResponse FromRoute(BusinessRuntimeRouteProjection route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return new RuntimeRouteResponse(name: route.Name, host: route.Host, pathPrefix: route.PathPrefix, action: RuntimeRouteActionResponseMapper.FromAction(route.Action), loadBalancingPolicy: route.LoadBalancingPolicy, healthCheck: RuntimeHealthCheckResponseMapper.FromProjection(route.HealthCheck), upstreams: RuntimeUpstreamResponseMapper.FromUpstreams(route.Upstreams), httpsRedirect: RuntimeHttpsRedirectResponseMapper.FromProjection(route.HttpsRedirect), canonicalHost: RuntimeCanonicalHostResponseMapper.FromProjection(route.CanonicalHost), headerPolicy: RuntimeHeaderPolicyResponseMapper.FromProjection(route.HeaderPolicy), pathRewrite: RuntimePathRewriteResponseMapper.FromProjection(route.PathRewrite), redirect: RuntimeRedirectResponseMapper.FromProjection(route.Redirect), staticResponse: RuntimeStaticResponseResponseMapper.FromProjection(route.StaticResponse), maintenance: RuntimeMaintenanceResponseMapper.FromProjection(route.Maintenance), cache: RuntimeCachePolicyResponseMapper.FromProjection(route.Cache), resolvedOptions: RuntimeRouteResolvedOptionsResponseMapper.FromProjection(route.ResolvedOptions), siteName: route.SiteName, retry: RuntimeRetryPolicyResponseMapper.FromProjection(route.Retry));
    }
}
