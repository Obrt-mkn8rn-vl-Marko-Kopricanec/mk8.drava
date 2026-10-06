namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed record ProxyCacheRequestScope
{
    public ProxyCacheRequestScope(string RouteName, string RouteHost, string Scheme, ProxyCachePolicyFacts Policy)
    {
        ArgumentNullException.ThrowIfNull(RouteName);
        ArgumentNullException.ThrowIfNull(RouteHost);
        ArgumentNullException.ThrowIfNull(Scheme);
        ArgumentNullException.ThrowIfNull(Policy);
        this.RouteName = RouteName;
        this.RouteHost = RouteHost;
        this.Scheme = Scheme;
        this.Policy = Policy;
    }

    public string RouteName { get; }
    public string RouteHost { get; }
    public string Scheme { get; }
    public ProxyCachePolicyFacts Policy { get; }
}
