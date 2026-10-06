using BusinessCircuitBreakerRuntimeState = Mk8.Drava.Application.BLL.ControlPlane.Resilience.CircuitBreakerRuntimeState;
using BusinessCircuitBreakerStatus = Mk8.Drava.Application.BLL.ControlPlane.Resilience.CircuitBreakerStatus;
using BusinessUpstreamHealthState = Mk8.Drava.Application.BLL.ControlPlane.HealthChecks.UpstreamHealthState;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class UpstreamHealthStateResponseMapper
{
    public static UpstreamHealthStateResponse FromState(BusinessUpstreamHealthState state)
    {
        return state switch
        {
            BusinessUpstreamHealthState.Unknown => UpstreamHealthStateResponse.Unknown,
            BusinessUpstreamHealthState.Healthy => UpstreamHealthStateResponse.Healthy,
            BusinessUpstreamHealthState.Unhealthy => UpstreamHealthStateResponse.Unhealthy,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)};
    }
}
