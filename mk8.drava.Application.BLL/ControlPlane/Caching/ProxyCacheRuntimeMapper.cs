using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public static class ProxyCacheRuntimeMapper
{
    public static ProxyCacheRequestScope ToRequestScope(RuntimeRoute route, RuntimeListener listener)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(listener);
        return new ProxyCacheRequestScope(route.Name, route.Host, RuntimeListenerTransportScheme.FromTransport(listener.Transport), ToPolicyFacts(route.Cache));
    }

    public static ProxyCachePolicyFacts ToPolicyFacts(RuntimeCachePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new ProxyCachePolicyFacts(policy.Enabled, policy.MaxEntryBytes, policy.MaxTotalBytes, policy.DefaultTtl, policy.RespectOriginCacheControl, policy.VaryByHeaders, policy.CacheableStatusCodes, policy.Methods);
    }
}
