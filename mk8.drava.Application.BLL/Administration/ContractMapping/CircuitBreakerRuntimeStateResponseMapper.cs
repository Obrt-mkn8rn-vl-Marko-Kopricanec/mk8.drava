using BusinessCircuitBreakerRuntimeState = Mk8.Drava.Application.BLL.ControlPlane.Resilience.CircuitBreakerRuntimeState;
using BusinessCircuitBreakerStatus = Mk8.Drava.Application.BLL.ControlPlane.Resilience.CircuitBreakerStatus;
using BusinessUpstreamHealthState = Mk8.Drava.Application.BLL.ControlPlane.HealthChecks.UpstreamHealthState;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class CircuitBreakerRuntimeStateResponseMapper
{
    public static CircuitBreakerRuntimeStateResponse FromState(BusinessCircuitBreakerRuntimeState state)
    {
        return state switch
        {
            BusinessCircuitBreakerRuntimeState.Disabled => CircuitBreakerRuntimeStateResponse.Disabled,
            BusinessCircuitBreakerRuntimeState.Closed => CircuitBreakerRuntimeStateResponse.Closed,
            BusinessCircuitBreakerRuntimeState.Open => CircuitBreakerRuntimeStateResponse.Open,
            BusinessCircuitBreakerRuntimeState.HalfOpen => CircuitBreakerRuntimeStateResponse.HalfOpen,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)};
    }
}
