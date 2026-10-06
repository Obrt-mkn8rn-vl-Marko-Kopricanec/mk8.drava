namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public sealed record RouteMatch
{
    public RouteMatch(int RouteIndex)
    {
        RouteMatchFacts.ValidateRouteIndex(RouteIndex);
        this.RouteIndex = RouteIndex;
    }

    public int RouteIndex { get; }
}
