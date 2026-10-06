using BusinessProxyCacheRejectionStatus = Mk8.Drava.Application.BLL.ControlPlane.Caching.ProxyCacheRejectionStatus;
using BusinessProxyCacheRouteStatus = Mk8.Drava.Application.BLL.ControlPlane.Caching.ProxyCacheRouteStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyCacheRejectionStatusResponseMapper
{
    public static IReadOnlyList<ProxyCacheRejectionStatusResponse> FromStatuses(IReadOnlyList<BusinessProxyCacheRejectionStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return ApiResponseList.Copy(statuses.Select(FromStatus));
    }

    private static ProxyCacheRejectionStatusResponse FromStatus(BusinessProxyCacheRejectionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProxyCacheRejectionStatusResponse(status.Reason, status.Count);
    }
}
