namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public sealed record RouteMatchCandidate
{
    public RouteMatchCandidate(string Host, string PathPrefix)
    {
        RouteMatchFacts.ValidateCandidate(Host, PathPrefix);
        this.Host = Host;
        this.PathPrefix = PathPrefix;
    }

    public string Host { get; }
    public string PathPrefix { get; }
}
