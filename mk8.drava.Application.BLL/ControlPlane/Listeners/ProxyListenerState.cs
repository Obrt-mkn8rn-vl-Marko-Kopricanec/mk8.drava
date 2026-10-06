namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public enum ProxyListenerState
{
    Starting = 0,
    Active = 1,
    Draining = 2,
    Stopped = 3,
    Failed = 4
}
