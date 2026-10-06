namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public sealed record RouteMatchRequest
{
    public RouteMatchRequest(string Host, string Path)
    {
        RouteMatchFacts.ValidateRequest(Host, Path);
        this.Host = Host;
        this.Path = Path;
    }

    public string Host { get; }
    public string Path { get; }
}
