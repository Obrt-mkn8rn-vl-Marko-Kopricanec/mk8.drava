using BusinessRouteMatchDryRunFinding = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunFinding;
using BusinessRouteMatchDryRunListener = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunListener;
using BusinessRouteMatchDryRunPolicy = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunPolicy;
using BusinessRouteMatchDryRunRoute = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunRoute;
using BusinessRouteMatchDryRunUpstream = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunUpstream;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RouteMatchDryRunListenerResponseMapper
{
    public static RouteMatchDryRunListenerResponse FromListener(BusinessRouteMatchDryRunListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new RouteMatchDryRunListenerResponse(listener.Name, listener.Transport, listener.Address, listener.Port, listener.Protocols);
    }
}
