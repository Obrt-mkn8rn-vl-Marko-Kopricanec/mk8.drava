namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public sealed record ProxyQuicListenerReloadTarget(string Key, string Address, int Port, string Transport, string Http3Enablement, bool Failed);
