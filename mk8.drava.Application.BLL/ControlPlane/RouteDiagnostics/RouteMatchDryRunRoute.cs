namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record RouteMatchDryRunRoute
{
    public RouteMatchDryRunRoute(string SiteName, string Name, string Host, string PathPrefix)
    {
        ArgumentNullException.ThrowIfNull(SiteName);
        ArgumentNullException.ThrowIfNull(Name);
        ArgumentNullException.ThrowIfNull(Host);
        ArgumentNullException.ThrowIfNull(PathPrefix);
        this.SiteName = SiteName;
        this.Name = Name;
        this.Host = Host;
        this.PathPrefix = PathPrefix;
    }

    public string SiteName { get; }
    public string Name { get; }
    public string Host { get; }
    public string PathPrefix { get; }
}
