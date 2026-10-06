using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeUpstream
{
    public RuntimeUpstream(string RouteName, string Name, string Scheme, string Protocol, string Address, int Port, int Weight, RuntimeUpstreamTlsOptions Tls) : this(RouteName, Name, Scheme, Protocol, Address, Port, Weight, Tls, RuntimeCircuitBreakerPolicy.Disabled)
    {
    }

    public RuntimeUpstream(string RouteName, string Name, string Scheme, string Protocol, string Address, int Port, int Weight, RuntimeUpstreamTlsOptions Tls, RuntimeCircuitBreakerPolicy CircuitBreaker, RegisteredUpstreamIdentity? Membership = null)
    {
        RuntimeUpstreamFacts.Validate(RouteName, Name, Scheme, Protocol, Address, Port, Weight);
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
        this.CircuitBreaker = CircuitBreaker;
        this.Membership = Membership;
    }

    public string RouteName { get; }
    public string Name { get; }
    public string Scheme { get; }
    public string Protocol { get; }
    public string Address { get; }
    public int Port { get; }
    public int Weight { get; }
    public RuntimeUpstreamTlsOptions Tls { get; }
    public string Endpoint => $"{Address}:{Port}";
    public string UriEndpoint => $"{Scheme}://{Address}:{Port}";
    public string EffectiveSniHost => string.IsNullOrWhiteSpace(Tls.SniHost) ? Address : Tls.SniHost!;
    public string Identity => $"{RouteName}|{Name}|{Scheme}|{Protocol}|{Address}|{Port}|{EffectiveSniHost}|{Tls.ValidateCertificate}" + (Membership is null ? "" : "|membership=" + Membership.Partition);
    public RegisteredUpstreamIdentity? Membership { get; }
    public RuntimeCircuitBreakerPolicy CircuitBreaker { get; }
}
