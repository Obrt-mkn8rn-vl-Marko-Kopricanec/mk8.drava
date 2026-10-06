namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyCacheStatusResponse
{
    public ProxyCacheStatusResponse(int entryCount, long approximateBytes, long hitCount, long missCount, long storeCount, long evictionCount, long storeRejectionCount, DateTimeOffset? lastClearedAtUtc, string? lastClearReason, IReadOnlyList<ProxyCacheRejectionStatusResponse> rejections, IReadOnlyList<ProxyCacheRouteStatusResponse> routes)
    {
        EntryCount = entryCount;
        ApproximateBytes = approximateBytes;
        HitCount = hitCount;
        MissCount = missCount;
        StoreCount = storeCount;
        EvictionCount = evictionCount;
        StoreRejectionCount = storeRejectionCount;
        LastClearedAtUtc = lastClearedAtUtc;
        LastClearReason = lastClearReason;
        Rejections = ApiResponseList.Copy(rejections);
        Routes = ApiResponseList.Copy(routes);
    }

    public int EntryCount { get; }
    public long ApproximateBytes { get; }
    public long HitCount { get; }
    public long MissCount { get; }
    public long StoreCount { get; }
    public long EvictionCount { get; }
    public long StoreRejectionCount { get; }
    public DateTimeOffset? LastClearedAtUtc { get; }
    public string? LastClearReason { get; }
    public IReadOnlyList<ProxyCacheRejectionStatusResponse> Rejections { get; }
    public IReadOnlyList<ProxyCacheRouteStatusResponse> Routes { get; }
}
