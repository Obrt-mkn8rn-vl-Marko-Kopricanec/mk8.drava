using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
public sealed record ProxyRouteTimeoutPolicyInput(TimeSpan UpstreamResponseHeadTimeout, TimeSpan? RetryPerAttemptTimeout);
