namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public sealed record RouteActionDecision(GeneratedRouteResponse? Response)
{
    public bool ShouldProxy => Response is null;
    public static RouteActionDecision Proxy { get; } = new((GeneratedRouteResponse? )null);
}
