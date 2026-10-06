using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
public sealed record SelectedUpstream(RuntimeUpstream Upstream, CircuitBreakerLease CircuitBreakerLease);
