namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public sealed record ProxyListenerReloadPlan(ProxyListenerDiff TcpDiff, ProxyListenerDiff QuicDiff);
