using BusinessProxyCacheStatus = Mk8.Drava.Application.BLL.ControlPlane.Caching.ProxyCacheStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyCacheStatusResponseMapper
{
    public static ProxyCacheStatusResponse FromStatus(BusinessProxyCacheStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProxyCacheStatusResponse(entryCount: status.EntryCount, approximateBytes: status.ApproximateBytes, hitCount: status.HitCount, missCount: status.MissCount, storeCount: status.StoreCount, evictionCount: status.EvictionCount, storeRejectionCount: status.StoreRejectionCount, lastClearedAtUtc: status.LastClearedAtUtc, lastClearReason: status.LastClearReason, rejections: ProxyCacheRejectionStatusResponseMapper.FromStatuses(status.Rejections), routes: ProxyCacheRouteStatusResponseMapper.FromStatuses(status.Routes));
    }
}
