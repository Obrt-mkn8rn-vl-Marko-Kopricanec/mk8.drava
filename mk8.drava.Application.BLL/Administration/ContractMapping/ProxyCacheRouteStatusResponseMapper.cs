using BusinessProxyCacheRejectionStatus = Mk8.Drava.Application.BLL.ControlPlane.Caching.ProxyCacheRejectionStatus;
using BusinessProxyCacheRouteStatus = Mk8.Drava.Application.BLL.ControlPlane.Caching.ProxyCacheRouteStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyCacheRouteStatusResponseMapper
{
    public static IReadOnlyList<ProxyCacheRouteStatusResponse> FromStatuses(IReadOnlyList<BusinessProxyCacheRouteStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return ApiResponseList.Copy(statuses.Select(FromStatus));
    }

    private static ProxyCacheRouteStatusResponse FromStatus(BusinessProxyCacheRouteStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProxyCacheRouteStatusResponse(status.RouteName, status.Enabled, status.MaxEntryBytes, status.MaxTotalBytes, status.CurrentEntryCount, status.CurrentBytes);
    }
}
