using BusinessRuntimeCacheProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeCacheProjection;
using BusinessRuntimeRetryProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeRetryProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeCachePolicyResponseMapper
{
    public static RuntimeCachePolicyResponse FromProjection(BusinessRuntimeCacheProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeCachePolicyResponse(enabled: projection.Enabled, maxEntryBytes: projection.MaxEntryBytes, maxTotalBytes: projection.MaxTotalBytes, defaultTtl: projection.DefaultTtl, respectOriginCacheControl: projection.RespectOriginCacheControl, varyByHeaders: projection.VaryByHeaders, cacheableStatusCodes: projection.CacheableStatusCodes, methods: projection.Methods);
    }
}
