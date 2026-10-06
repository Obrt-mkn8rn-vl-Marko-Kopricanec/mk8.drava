using BusinessRuntimeCircuitBreakerProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeCircuitBreakerProjection;
using BusinessRuntimeUpstreamProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeUpstreamProjection;
using BusinessRuntimeUpstreamTlsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeUpstreamTlsProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeUpstreamTlsResponseMapper
{
    public static RuntimeUpstreamTlsResponse FromProjection(BusinessRuntimeUpstreamTlsProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeUpstreamTlsResponse(projection.ValidateCertificate, projection.SniHost);
    }
}
