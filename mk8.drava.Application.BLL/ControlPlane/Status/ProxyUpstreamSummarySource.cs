using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyUpstreamSummarySource(UpstreamHealthState HealthState, bool HealthCheckEnabled, bool CircuitBreakerEnabled, CircuitBreakerRuntimeState CircuitBreakerState);
