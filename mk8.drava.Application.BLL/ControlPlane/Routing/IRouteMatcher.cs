namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public interface IRouteMatcher
{
    RouteMatch? Match(IReadOnlyList<RouteMatchCandidate> routes, RouteMatchRequest request);
}
