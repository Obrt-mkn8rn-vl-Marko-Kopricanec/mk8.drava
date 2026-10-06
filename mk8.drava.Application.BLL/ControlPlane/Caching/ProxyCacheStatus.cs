namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed record ProxyCacheStatus
{
    private ProxyCacheStatus(int entryCount, long approximateBytes, long hitCount, long missCount, long storeCount, long evictionCount, long storeRejectionCount, DateTimeOffset? lastClearedAtUtc, string? lastClearReason, IEnumerable<ProxyCacheRejectionStatus> rejections, IEnumerable<ProxyCacheRouteStatus> routes)
    {
        ArgumentNullException.ThrowIfNull(rejections);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentOutOfRangeException.ThrowIfNegative(entryCount);
        ArgumentOutOfRangeException.ThrowIfNegative(approximateBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(hitCount);
        ArgumentOutOfRangeException.ThrowIfNegative(missCount);
        ArgumentOutOfRangeException.ThrowIfNegative(storeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(evictionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(storeRejectionCount);
        EntryCount = entryCount;
        ApproximateBytes = approximateBytes;
        HitCount = hitCount;
        MissCount = missCount;
        StoreCount = storeCount;
        EvictionCount = evictionCount;
        StoreRejectionCount = storeRejectionCount;
        LastClearedAtUtc = lastClearedAtUtc;
        LastClearReason = lastClearReason;
        Rejections = CacheList.Copy(rejections);
        Routes = CacheList.Copy(routes);
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
    public IReadOnlyList<ProxyCacheRejectionStatus> Rejections { get; }
    public IReadOnlyList<ProxyCacheRouteStatus> Routes { get; }

    public static ProxyCacheStatus FromSources(int entryCount, long approximateBytes, long hitCount, long missCount, long storeCount, long evictionCount, long storeRejectionCount, DateTimeOffset? lastClearedAtUtc, string? lastClearReason, IEnumerable<ProxyCacheRejectionStatus> rejections, IEnumerable<ProxyCacheRouteStatus> routes)
    {
        ArgumentNullException.ThrowIfNull(rejections);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentOutOfRangeException.ThrowIfNegative(entryCount);
        ArgumentOutOfRangeException.ThrowIfNegative(approximateBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(hitCount);
        ArgumentOutOfRangeException.ThrowIfNegative(missCount);
        ArgumentOutOfRangeException.ThrowIfNegative(storeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(evictionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(storeRejectionCount);
        return new ProxyCacheStatus(entryCount, approximateBytes, hitCount, missCount, storeCount, evictionCount, storeRejectionCount, lastClearedAtUtc, lastClearReason, rejections, routes);
    }
}
