namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public sealed record ProxyListenerReloadChange(string Action, string Name, string Identity, string BindKey, string State, string? Error = null)
{
    public static ProxyListenerReloadChange FromStatus(string action, ProxyListenerStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(status);
        return new ProxyListenerReloadChange(action, status.Name, status.Identity, status.BindKey, ProxyListenerStateText.FromState(status.State), status.LastError);
    }
}
