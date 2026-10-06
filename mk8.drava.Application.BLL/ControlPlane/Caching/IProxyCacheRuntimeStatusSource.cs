namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public interface IProxyCacheRuntimeStatusSource
{
    ProxyCacheRuntimeStatusSnapshot ReadSnapshot();
}
