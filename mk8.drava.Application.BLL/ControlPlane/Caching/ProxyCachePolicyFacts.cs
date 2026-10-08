namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed record ProxyCachePolicyFacts
{
    public ProxyCachePolicyFacts(bool Enabled, long MaxEntryBytes, long MaxTotalBytes, TimeSpan DefaultTtl, bool RespectOriginCacheControl, IReadOnlyList<string> VaryByHeaders, IReadOnlyList<int> CacheableStatusCodes, IReadOnlyList<string> Methods)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTotalBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(DefaultTtl, TimeSpan.Zero);

        ArgumentNullException.ThrowIfNull(VaryByHeaders);
        ArgumentNullException.ThrowIfNull(CacheableStatusCodes);
        ArgumentNullException.ThrowIfNull(Methods);
        this.Enabled = Enabled;
        this.MaxEntryBytes = MaxEntryBytes;
        this.MaxTotalBytes = MaxTotalBytes;
        this.DefaultTtl = DefaultTtl;
        this.RespectOriginCacheControl = RespectOriginCacheControl;
        this.VaryByHeaders = CacheList.Copy(VaryByHeaders);
        this.CacheableStatusCodes = CacheList.Copy(CacheableStatusCodes);
        this.Methods = CacheList.Copy(Methods);
    }

    public bool Enabled { get; }
    public long MaxEntryBytes { get; }
    public long MaxTotalBytes { get; }
    public TimeSpan DefaultTtl { get; }
    public bool RespectOriginCacheControl { get; }
    public IReadOnlyList<string> VaryByHeaders { get; }
    public IReadOnlyList<int> CacheableStatusCodes { get; }
    public IReadOnlyList<string> Methods { get; }
}
