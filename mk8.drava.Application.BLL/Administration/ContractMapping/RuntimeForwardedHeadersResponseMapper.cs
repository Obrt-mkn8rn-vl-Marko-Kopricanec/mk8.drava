using BusinessRuntimeConnectionLimitsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeConnectionLimitsProjection;
using BusinessRuntimeForwardedHeadersProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeForwardedHeadersProjection;
using BusinessRuntimeLimitsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeLimitsProjection;
using BusinessRuntimeTimeoutsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeTimeoutsProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeForwardedHeadersResponseMapper
{
    public static RuntimeForwardedHeadersResponse FromProjection(BusinessRuntimeForwardedHeadersProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeForwardedHeadersResponse(enabled: projection.Enabled, trustedProxies: projection.TrustedProxies);
    }
}
