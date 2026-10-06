using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;

namespace Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
public static class CircuitBreakerStatusSourceMapper
{
    public static CircuitBreakerStatusSource FromUpstream(RuntimeUpstream upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        return new CircuitBreakerStatusSource(upstream.Identity, new CircuitBreakerPolicyInput(upstream.CircuitBreaker.Enabled, upstream.CircuitBreaker.FailureThreshold, upstream.CircuitBreaker.SamplingWindow, upstream.CircuitBreaker.OpenDuration, upstream.CircuitBreaker.HalfOpenMaxAttempts, upstream.CircuitBreaker.FailureStatusCodes));
    }
}
