using BusinessRuntimeCanonicalHostProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeCanonicalHostProjection;
using BusinessRuntimeHttpsRedirectProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHttpsRedirectProjection;
using BusinessRuntimeMaintenanceProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeMaintenanceProjection;
using BusinessRuntimePathRewriteProjection = Mk8.Drava.Application.BLL.Configuration.RuntimePathRewriteProjection;
using BusinessRuntimeRedirectProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeRedirectProjection;
using BusinessRuntimeStaticResponseProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeStaticResponseProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimePathRewriteResponseMapper
{
    public static RuntimePathRewriteResponse FromProjection(BusinessRuntimePathRewriteProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimePathRewriteResponse(projection.StripPrefix, projection.ReplacePrefix, projection.Replacement);
    }
}
