using BusinessRuntimeAdminSecurityProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeAdminSecurityProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeAdminSecurityResponseMapper
{
    public static RuntimeAdminSecurityResponse FromProjection(BusinessRuntimeAdminSecurityProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeAdminSecurityResponse(urls: projection.Urls, requireAuthentication: projection.RequireAuthentication, hasConfiguredToken: projection.HasConfiguredToken, token: projection.Token, tokenEnvironmentVariable: projection.TokenEnvironmentVariable, tokenSource: projection.TokenSource, recentAuditCapacity: projection.RecentAuditCapacity);
    }
}
