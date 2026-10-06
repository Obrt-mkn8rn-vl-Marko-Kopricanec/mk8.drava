namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeUpstreamProjection
{
    public RuntimeUpstreamProjection(string RouteName, string Name, string Scheme, string Protocol, string Address, int Port, int Weight, RuntimeUpstreamTlsProjection Tls, string Endpoint, string UriEndpoint, string EffectiveSniHost, string Identity, RuntimeCircuitBreakerProjection CircuitBreaker)
    {
        RuntimeUpstreamFacts.ValidateProjection(RouteName, Name, Scheme, Protocol, Address, Port, Weight, Endpoint, UriEndpoint, EffectiveSniHost, Identity);
        ArgumentNullException.ThrowIfNull(Tls);
        ArgumentNullException.ThrowIfNull(CircuitBreaker);
        this.RouteName = RouteName;
        this.Name = Name;
        this.Scheme = Scheme;
        this.Protocol = Protocol;
        this.Address = Address;
        this.Port = Port;
        this.Weight = Weight;
        this.Tls = Tls;
        this.Endpoint = Endpoint;
        this.UriEndpoint = UriEndpoint;
        this.EffectiveSniHost = EffectiveSniHost;
        this.Identity = Identity;
        this.CircuitBreaker = CircuitBreaker;
    }

    public string RouteName { get; }
    public string Name { get; }
    public string Scheme { get; }
    public string Protocol { get; }
    public string Address { get; }
    public int Port { get; }
    public int Weight { get; }
    public RuntimeUpstreamTlsProjection Tls { get; }
    public string Endpoint { get; }
    public string UriEndpoint { get; }
    public string EffectiveSniHost { get; }
    public string Identity { get; }
    public RuntimeCircuitBreakerProjection CircuitBreaker { get; }
}
