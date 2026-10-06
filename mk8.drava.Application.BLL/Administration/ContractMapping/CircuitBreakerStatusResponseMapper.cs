using BusinessCircuitBreakerRuntimeState = Mk8.Drava.Application.BLL.ControlPlane.Resilience.CircuitBreakerRuntimeState;
using BusinessCircuitBreakerStatus = Mk8.Drava.Application.BLL.ControlPlane.Resilience.CircuitBreakerStatus;
using BusinessUpstreamHealthState = Mk8.Drava.Application.BLL.ControlPlane.HealthChecks.UpstreamHealthState;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class CircuitBreakerStatusResponseMapper
{
    public static CircuitBreakerStatusResponse FromStatus(BusinessCircuitBreakerStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new CircuitBreakerStatusResponse(CircuitBreakerRuntimeStateResponseMapper.FromState(status.State), status.Enabled, status.FailureThreshold, status.HalfOpenMaxAttempts, status.OpenedAtUtc, status.NextAttemptAtUtc, status.FailureCount, status.RejectedRequests, status.LastFailureReason);
    }
}
