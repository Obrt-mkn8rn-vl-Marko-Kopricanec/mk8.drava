using BusinessRuntimeHealthCheckProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHealthCheckProjection;
using BusinessRuntimeRouteAction = Mk8.Drava.Application.BLL.Configuration.RuntimeRouteAction;
using BusinessRuntimeRouteProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeRouteProjection;
using BusinessRuntimeRouteResolvedOptionsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeRouteResolvedOptionsProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeRouteResolvedOptionsResponseMapper
{
    public static RuntimeRouteResolvedOptionsResponse FromProjection(BusinessRuntimeRouteResolvedOptionsProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeRouteResolvedOptionsResponse(projection.MaxRequestBodyBytes, projection.ClientRequestHeadTimeout, projection.UpstreamResponseHeadTimeout, projection.AccessLogEnabled);
    }
}
