using BusinessRuntimeHealthCheckProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHealthCheckProjection;
using BusinessRuntimeRouteAction = Mk8.Drava.Application.BLL.Configuration.RuntimeRouteAction;
using BusinessRuntimeRouteProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeRouteProjection;
using BusinessRuntimeRouteResolvedOptionsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeRouteResolvedOptionsProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeRouteActionResponseMapper
{
    public static RuntimeRouteActionResponse FromAction(BusinessRuntimeRouteAction action)
    {
        return action switch
        {
            BusinessRuntimeRouteAction.Proxy => RuntimeRouteActionResponse.Proxy,
            BusinessRuntimeRouteAction.Redirect => RuntimeRouteActionResponse.Redirect,
            BusinessRuntimeRouteAction.StaticResponse => RuntimeRouteActionResponse.StaticResponse,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)};
    }
}
