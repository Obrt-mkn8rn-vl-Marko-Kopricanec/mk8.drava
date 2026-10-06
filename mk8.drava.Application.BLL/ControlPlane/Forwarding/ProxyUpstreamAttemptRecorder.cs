using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using SelectedUpstream = Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection.SelectedUpstream;

namespace Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
public static class ProxyUpstreamAttemptRecorder
{
    public static void Record(SelectedUpstream selection, ForwardingResult result, UpstreamHealthStore healthStore, CircuitBreakerStore circuitBreakerStore)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(healthStore);
        ArgumentNullException.ThrowIfNull(circuitBreakerStore);
        if (result.ResponseStatusCode.HasValue && selection.Upstream.CircuitBreaker.FailureStatusCodes.Any(code => code == result.ResponseStatusCode.Value))
        {
            circuitBreakerStore.RecordFailure(selection.CircuitBreakerLease, "status_code", result.ResponseStatusCode);
            return;
        }

        if (result is ForwardingResult.FailureResult)
        {
            healthStore.RecordRequestFailure(UpstreamHealthStateSourceMapper.FromUpstream(selection.Upstream));
            if (ProxyForwardingFailurePolicy.IsCircuitFailure(result.FailureKind))
            {
                circuitBreakerStore.RecordFailure(selection.CircuitBreakerLease, ProxyForwardingFailurePolicy.CircuitFailureReason(result.FailureKind));
            }
            else
            {
                selection.CircuitBreakerLease.Dispose();
            }

            return;
        }

        circuitBreakerStore.RecordSuccess(selection.CircuitBreakerLease);
    }
}
