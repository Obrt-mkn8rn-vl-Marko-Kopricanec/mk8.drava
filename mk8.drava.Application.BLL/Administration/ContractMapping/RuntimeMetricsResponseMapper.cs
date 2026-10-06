using BusinessRuntimeLogPersistenceProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeLogPersistenceProjection;
using BusinessRuntimeMetricsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeMetricsProjection;
using BusinessRuntimeObservabilityProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeObservabilityProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeMetricsResponseMapper
{
    public static RuntimeMetricsResponse FromProjection(BusinessRuntimeMetricsProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeMetricsResponse(projection.Enabled, projection.EndpointPath, projection.ProtectedByAdminAuth, projection.IncludePerRouteLabels, projection.IncludePerUpstreamLabels, projection.PublicMetricsEnabled);
    }
}
