using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public sealed record Http3SupportRuntimeListenerSource(bool IsQuic, string Identity, ProxyListenerState State);
