namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeUpstreamResponse
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054", Justification = "Versioned administration wire contract preserves raw URI text; Application validates syntax and ownership before use.")]
    public RuntimeUpstreamResponse(string routeName, string name, string scheme, string protocol, string address, int port, int weight, RuntimeUpstreamTlsResponse tls, string endpoint, string uriEndpoint, string effectiveSniHost, string identity, RuntimeCircuitBreakerResponse circuitBreaker)
    {
        ArgumentNullException.ThrowIfNull(tls);
        ArgumentNullException.ThrowIfNull(circuitBreaker);
        RouteName = routeName;
        Name = name;
        Scheme = scheme;
        Protocol = protocol;
        Address = address;
        Port = port;
        Weight = weight;
        Tls = tls;
        Endpoint = endpoint;
        UriEndpoint = uriEndpoint;
        EffectiveSniHost = effectiveSniHost;
        Identity = identity;
        CircuitBreaker = circuitBreaker;
    }

    public string RouteName { get; }
    public string Name { get; }
    public string Scheme { get; }
    public string Protocol { get; }
    public string Address { get; }
    public int Port { get; }
    public int Weight { get; }
    public RuntimeUpstreamTlsResponse Tls { get; }
    public string Endpoint { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056", Justification = "Versioned wire URI text; not used as a destination without Application validation.")]
    public string UriEndpoint { get; }
    public string EffectiveSniHost { get; }
    public string Identity { get; }
    public RuntimeCircuitBreakerResponse CircuitBreaker { get; }
}
