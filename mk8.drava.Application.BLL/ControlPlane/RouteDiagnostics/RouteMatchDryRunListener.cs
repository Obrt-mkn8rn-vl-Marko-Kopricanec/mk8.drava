namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record RouteMatchDryRunListener
{
    public RouteMatchDryRunListener(string Name, string Transport, string Address, int Port, string Protocols)
    {
        ArgumentNullException.ThrowIfNull(Name);
        ArgumentNullException.ThrowIfNull(Transport);
        ArgumentNullException.ThrowIfNull(Address);
        ArgumentNullException.ThrowIfNull(Protocols);
        this.Name = Name;
        this.Transport = Transport;
        this.Address = Address;
        this.Port = Port;
        this.Protocols = Protocols;
    }

    public string Name { get; }
    public string Transport { get; }
    public string Address { get; }
    public int Port { get; }
    public string Protocols { get; }
}
