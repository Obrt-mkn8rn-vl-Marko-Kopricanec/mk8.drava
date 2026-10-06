namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record RouteMatchDryRunUpstream
{
    public RouteMatchDryRunUpstream(string Name, string Scheme, string Protocol, string Endpoint, int Weight, string SelectionReason)
    {
        ArgumentNullException.ThrowIfNull(Name);
        ArgumentNullException.ThrowIfNull(Scheme);
        ArgumentNullException.ThrowIfNull(Protocol);
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentNullException.ThrowIfNull(SelectionReason);
        this.Name = Name;
        this.Scheme = Scheme;
        this.Protocol = Protocol;
        this.Endpoint = Endpoint;
        this.Weight = Weight;
        this.SelectionReason = SelectionReason;
    }

    public string Name { get; }
    public string Scheme { get; }
    public string Protocol { get; }
    public string Endpoint { get; }
    public int Weight { get; }
    public string SelectionReason { get; }
}
