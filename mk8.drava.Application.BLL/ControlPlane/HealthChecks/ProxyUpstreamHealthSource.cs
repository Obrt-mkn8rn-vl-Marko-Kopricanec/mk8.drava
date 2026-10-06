using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;

namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public sealed record ProxyUpstreamHealthSource(UpstreamHealthStateSource HealthState, CircuitBreakerStatusSource CircuitBreaker, string Scheme, string Protocol, int Weight, bool ValidateCertificate, string? EffectiveSniHost, bool HealthCheckEnabled);
