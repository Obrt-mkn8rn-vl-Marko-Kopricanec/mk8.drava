using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;

namespace Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
public sealed record ProxyTimeoutFailure(int? ResponseStatusCode, ProxyFailureKind FailureKind);
