namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public static class ProxyListenerStateText
{
    public static string FromState(ProxyListenerState state)
    {
        return state switch
        {
            ProxyListenerState.Starting => nameof(ProxyListenerState.Starting),
            ProxyListenerState.Active => nameof(ProxyListenerState.Active),
            ProxyListenerState.Draining => nameof(ProxyListenerState.Draining),
            ProxyListenerState.Stopped => nameof(ProxyListenerState.Stopped),
            ProxyListenerState.Failed => nameof(ProxyListenerState.Failed),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown proxy listener state.")};
    }
}
