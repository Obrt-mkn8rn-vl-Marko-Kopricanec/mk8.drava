namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed record ProxyCacheRuntimeStatusSnapshot
{
    public ProxyCacheRuntimeStatusSnapshot(int EntryCount, long ApproximateBytes, long HitCount, long MissCount, long StoreCount, long EvictionCount, long StoreRejectionCount, DateTimeOffset? LastClearedAtUtc, string? LastClearReason, IReadOnlyList<ProxyCacheRuntimeRejectionSnapshot> Rejections, IReadOnlyList<ProxyCacheRuntimeEntrySnapshot> Entries)
    {
        ArgumentNullException.ThrowIfNull(Rejections);
        ArgumentNullException.ThrowIfNull(Entries);
        ArgumentOutOfRangeException.ThrowIfNegative(EntryCount);
        ArgumentOutOfRangeException.ThrowIfNegative(ApproximateBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(HitCount);
        ArgumentOutOfRangeException.ThrowIfNegative(MissCount);
        ArgumentOutOfRangeException.ThrowIfNegative(StoreCount);
        ArgumentOutOfRangeException.ThrowIfNegative(EvictionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(StoreRejectionCount);
        this.EntryCount = EntryCount;
        this.ApproximateBytes = ApproximateBytes;
        this.HitCount = HitCount;
        this.MissCount = MissCount;
        this.StoreCount = StoreCount;
        this.EvictionCount = EvictionCount;
        this.StoreRejectionCount = StoreRejectionCount;
        this.LastClearedAtUtc = LastClearedAtUtc;
        this.LastClearReason = LastClearReason;
        this.Rejections = CacheList.Copy(Rejections);
        this.Entries = CacheList.Copy(Entries);
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
    public IReadOnlyList<ProxyCacheRuntimeRejectionSnapshot> Rejections { get; }
    public IReadOnlyList<ProxyCacheRuntimeEntrySnapshot> Entries { get; }
}
