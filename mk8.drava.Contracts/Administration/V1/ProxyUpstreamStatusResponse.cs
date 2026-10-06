namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyUpstreamStatusResponse
{
    public ProxyUpstreamStatusResponse(string routeName, string upstreamName, string endpoint, string scheme, bool tlsCertificateValidationEnabled, string? sniHost, bool healthCheckEnabled, UpstreamHealthStateResponse healthState, string? lastHealthCheckResult, DateTimeOffset? lastHealthCheckAtUtc, int consecutiveSuccesses, int consecutiveFailures, long selectedRequests, long requestFailures) : this(routeName, upstreamName, endpoint, scheme, tlsCertificateValidationEnabled, sniHost, healthCheckEnabled, healthState, lastHealthCheckResult, lastHealthCheckAtUtc, consecutiveSuccesses, consecutiveFailures, selectedRequests, requestFailures, protocol: "http1", weight: 1, circuitBreaker: CircuitBreakerStatusResponse.Disabled)
    {
    }

    public ProxyUpstreamStatusResponse(string routeName, string upstreamName, string endpoint, string scheme, bool tlsCertificateValidationEnabled, string? sniHost, bool healthCheckEnabled, UpstreamHealthStateResponse healthState, string? lastHealthCheckResult, DateTimeOffset? lastHealthCheckAtUtc, int consecutiveSuccesses, int consecutiveFailures, long selectedRequests, long requestFailures, string protocol, int weight, CircuitBreakerStatusResponse circuitBreaker)
    {
        ArgumentNullException.ThrowIfNull(routeName);
        ArgumentNullException.ThrowIfNull(upstreamName);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(circuitBreaker);
        RouteName = routeName;
        UpstreamName = upstreamName;
        Endpoint = endpoint;
        Scheme = scheme;
        TlsCertificateValidationEnabled = tlsCertificateValidationEnabled;
        SniHost = sniHost;
        HealthCheckEnabled = healthCheckEnabled;
        HealthState = healthState;
        LastHealthCheckResult = lastHealthCheckResult;
        LastHealthCheckAtUtc = lastHealthCheckAtUtc;
        ConsecutiveSuccesses = consecutiveSuccesses;
        ConsecutiveFailures = consecutiveFailures;
        SelectedRequests = selectedRequests;
        RequestFailures = requestFailures;
        Protocol = protocol;
        Weight = weight;
        CircuitBreaker = circuitBreaker;
    }

    public string RouteName { get; }
    public string UpstreamName { get; }
    public string Endpoint { get; }
    public string Scheme { get; }
    public bool TlsCertificateValidationEnabled { get; }
    public string? SniHost { get; }
    public bool HealthCheckEnabled { get; }
    public UpstreamHealthStateResponse HealthState { get; }
    public string? LastHealthCheckResult { get; }
    public DateTimeOffset? LastHealthCheckAtUtc { get; }
    public int ConsecutiveSuccesses { get; }
    public int ConsecutiveFailures { get; }
    public long SelectedRequests { get; }
    public long RequestFailures { get; }
    public string Protocol { get; }
    public int Weight { get; }
    public CircuitBreakerStatusResponse CircuitBreaker { get; }
}
