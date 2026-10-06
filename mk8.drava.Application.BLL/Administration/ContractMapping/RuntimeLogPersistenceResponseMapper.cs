using BusinessRuntimeLogPersistenceProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeLogPersistenceProjection;
using BusinessRuntimeMetricsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeMetricsProjection;
using BusinessRuntimeObservabilityProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeObservabilityProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeLogPersistenceResponseMapper
{
    public static RuntimeLogPersistenceResponse FromProjection(BusinessRuntimeLogPersistenceProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeLogPersistenceResponse(projection.AccessLogEnabled, projection.AdminAuditEnabled, projection.MaxFileBytes, projection.MaxFiles);
    }
}
