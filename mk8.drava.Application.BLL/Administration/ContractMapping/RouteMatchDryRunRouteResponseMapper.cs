using BusinessRouteMatchDryRunFinding = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunFinding;
using BusinessRouteMatchDryRunListener = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunListener;
using BusinessRouteMatchDryRunPolicy = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunPolicy;
using BusinessRouteMatchDryRunRoute = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunRoute;
using BusinessRouteMatchDryRunUpstream = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunUpstream;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RouteMatchDryRunRouteResponseMapper
{
    public static RouteMatchDryRunRouteResponse FromRoute(BusinessRouteMatchDryRunRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return new RouteMatchDryRunRouteResponse(route.SiteName, route.Name, route.Host, route.PathPrefix);
    }
}
