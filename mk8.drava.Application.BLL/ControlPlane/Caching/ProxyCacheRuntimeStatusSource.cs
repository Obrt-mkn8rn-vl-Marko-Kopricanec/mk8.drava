namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed class ProxyCacheRuntimeStatusSource : IProxyCacheRuntimeStatusSource
{
    private readonly ResponseCacheStore _cacheStore;
    public ProxyCacheRuntimeStatusSource(ResponseCacheStore cacheStore)
    {
        _cacheStore = cacheStore;
    }

    public ProxyCacheRuntimeStatusSnapshot ReadSnapshot()
    {
        return _cacheStore.ReadStatusSnapshot();
    }
}
