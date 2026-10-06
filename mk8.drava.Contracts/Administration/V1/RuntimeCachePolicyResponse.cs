namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeCachePolicyResponse
{
    public RuntimeCachePolicyResponse(bool enabled, long maxEntryBytes, long maxTotalBytes, TimeSpan defaultTtl, bool respectOriginCacheControl, IReadOnlyList<string> varyByHeaders, IReadOnlyList<int> cacheableStatusCodes, IReadOnlyList<string> methods)
    {
        Enabled = enabled;
        MaxEntryBytes = maxEntryBytes;
        MaxTotalBytes = maxTotalBytes;
        DefaultTtl = defaultTtl;
        RespectOriginCacheControl = respectOriginCacheControl;
        VaryByHeaders = ApiResponseList.Copy(varyByHeaders);
        CacheableStatusCodes = ApiResponseList.Copy(cacheableStatusCodes);
        Methods = ApiResponseList.Copy(methods);
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
