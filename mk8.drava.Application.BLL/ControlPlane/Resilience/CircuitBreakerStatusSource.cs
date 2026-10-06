using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public sealed record CircuitBreakerStatusSource
{
    public CircuitBreakerStatusSource(string UpstreamIdentity, CircuitBreakerPolicyInput Policy)
    {
        ArgumentNullException.ThrowIfNull(UpstreamIdentity);
        ArgumentNullException.ThrowIfNull(Policy);
        this.UpstreamIdentity = UpstreamIdentity;
        this.Policy = Policy;
    }

    public string UpstreamIdentity { get; }
    public CircuitBreakerPolicyInput Policy { get; }
}
