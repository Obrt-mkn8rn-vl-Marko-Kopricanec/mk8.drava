using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyUpstreamStatus
{
    public ProxyUpstreamStatus(string RouteName, string UpstreamName, string Endpoint, string Scheme, bool TlsCertificateValidationEnabled, string? SniHost, bool HealthCheckEnabled, UpstreamHealthState HealthState, string? LastHealthCheckResult, DateTimeOffset? LastHealthCheckAtUtc, int ConsecutiveSuccesses, int ConsecutiveFailures, long SelectedRequests, long RequestFailures) : this(RouteName, UpstreamName, Endpoint, Scheme, TlsCertificateValidationEnabled, SniHost, HealthCheckEnabled, HealthState, LastHealthCheckResult, LastHealthCheckAtUtc, ConsecutiveSuccesses, ConsecutiveFailures, SelectedRequests, RequestFailures, Protocol: RuntimeUpstreamProtocol.Http1, Weight: 1, CircuitBreaker: CircuitBreakerStatus.Disabled(CircuitBreakerPolicyInput.Disabled))
    {
    }

    public ProxyUpstreamStatus(string RouteName, string UpstreamName, string Endpoint, string Scheme, bool TlsCertificateValidationEnabled, string? SniHost, bool HealthCheckEnabled, UpstreamHealthState HealthState, string? LastHealthCheckResult, DateTimeOffset? LastHealthCheckAtUtc, int ConsecutiveSuccesses, int ConsecutiveFailures, long SelectedRequests, long RequestFailures, string Protocol, int Weight, CircuitBreakerStatus CircuitBreaker)
    {
        ArgumentNullException.ThrowIfNull(RouteName);
        ArgumentNullException.ThrowIfNull(UpstreamName);
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentNullException.ThrowIfNull(Scheme);
        ArgumentNullException.ThrowIfNull(Protocol);
        ArgumentNullException.ThrowIfNull(CircuitBreaker);
        this.RouteName = RouteName;
        this.UpstreamName = UpstreamName;
        this.Endpoint = Endpoint;
        this.Scheme = Scheme;
        this.TlsCertificateValidationEnabled = TlsCertificateValidationEnabled;
        this.SniHost = SniHost;
        this.HealthCheckEnabled = HealthCheckEnabled;
        this.HealthState = HealthState;
        this.LastHealthCheckResult = LastHealthCheckResult;
        this.LastHealthCheckAtUtc = LastHealthCheckAtUtc;
        this.ConsecutiveSuccesses = ConsecutiveSuccesses;
        this.ConsecutiveFailures = ConsecutiveFailures;
        this.SelectedRequests = SelectedRequests;
        this.RequestFailures = RequestFailures;
        this.Protocol = Protocol;
        this.Weight = Weight;
        this.CircuitBreaker = CircuitBreaker;
    }

    public string RouteName { get; }
    public string UpstreamName { get; }
    public string Endpoint { get; }
    public string Scheme { get; }
    public bool TlsCertificateValidationEnabled { get; }
    public string? SniHost { get; }
    public bool HealthCheckEnabled { get; }
    public UpstreamHealthState HealthState { get; }
    public string? LastHealthCheckResult { get; }
    public DateTimeOffset? LastHealthCheckAtUtc { get; }
    public int ConsecutiveSuccesses { get; }
    public int ConsecutiveFailures { get; }
    public long SelectedRequests { get; }
    public long RequestFailures { get; }
    public string Protocol { get; }
    public int Weight { get; }
    public CircuitBreakerStatus CircuitBreaker { get; }
}
