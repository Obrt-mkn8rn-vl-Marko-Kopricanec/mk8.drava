using BusinessRuntimeHealthCheckProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHealthCheckProjection;
using BusinessRuntimeRouteAction = Mk8.Drava.Application.BLL.Configuration.RuntimeRouteAction;
using BusinessRuntimeRouteProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeRouteProjection;
using BusinessRuntimeRouteResolvedOptionsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeRouteResolvedOptionsProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeHealthCheckResponseMapper
{
    public static RuntimeHealthCheckResponse FromProjection(BusinessRuntimeHealthCheckProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeHealthCheckResponse(projection.Enabled, projection.Path, projection.Interval, projection.Timeout, projection.HealthyThreshold, projection.UnhealthyThreshold);
    }
}
