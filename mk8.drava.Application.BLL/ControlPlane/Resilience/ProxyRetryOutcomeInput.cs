using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public sealed record ProxyRetryOutcomeInput
{
    public ProxyRetryOutcomeInput(bool Enabled, bool RetryOnConnectFailure, bool RetryOnUpstreamResponseHeadTimeout, IReadOnlyList<int> RetryOnStatusCodes)
    {
        ArgumentNullException.ThrowIfNull(RetryOnStatusCodes);
        this.Enabled = Enabled;
        this.RetryOnConnectFailure = RetryOnConnectFailure;
        this.RetryOnUpstreamResponseHeadTimeout = RetryOnUpstreamResponseHeadTimeout;
        this.RetryOnStatusCodes = new ReadOnlyCollection<int>(RetryOnStatusCodes.ToArray());
    }

    public bool Enabled { get; }
    public bool RetryOnConnectFailure { get; }
    public bool RetryOnUpstreamResponseHeadTimeout { get; }
    public IReadOnlyList<int> RetryOnStatusCodes { get; }
}
