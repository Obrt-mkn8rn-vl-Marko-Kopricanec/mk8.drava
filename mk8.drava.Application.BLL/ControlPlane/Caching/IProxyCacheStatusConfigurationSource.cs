namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public interface IProxyCacheStatusConfigurationSource
{
    IReadOnlyList<ProxyCacheStatusRouteSource> ReadRoutes();
}
