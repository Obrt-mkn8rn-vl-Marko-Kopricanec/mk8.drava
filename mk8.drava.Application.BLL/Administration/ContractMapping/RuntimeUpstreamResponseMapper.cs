using BusinessRuntimeCircuitBreakerProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeCircuitBreakerProjection;
using BusinessRuntimeUpstreamProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeUpstreamProjection;
using BusinessRuntimeUpstreamTlsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeUpstreamTlsProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeUpstreamResponseMapper
{
    public static IReadOnlyList<RuntimeUpstreamResponse> FromUpstreams(IReadOnlyList<BusinessRuntimeUpstreamProjection> upstreams)
    {
        ArgumentNullException.ThrowIfNull(upstreams);
        return ApiResponseList.Copy(upstreams.Select(FromUpstream));
    }

    private static RuntimeUpstreamResponse FromUpstream(BusinessRuntimeUpstreamProjection upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        return new RuntimeUpstreamResponse(routeName: upstream.RouteName, name: upstream.Name, scheme: upstream.Scheme, protocol: upstream.Protocol, address: upstream.Address, port: upstream.Port, weight: upstream.Weight, tls: RuntimeUpstreamTlsResponseMapper.FromProjection(upstream.Tls), endpoint: upstream.Endpoint, uriEndpoint: upstream.UriEndpoint, effectiveSniHost: upstream.EffectiveSniHost, identity: upstream.Identity, circuitBreaker: RuntimeCircuitBreakerResponseMapper.FromProjection(upstream.CircuitBreaker));
    }
}
