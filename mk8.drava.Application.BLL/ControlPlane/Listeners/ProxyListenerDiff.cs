namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public sealed record ProxyListenerDiff
{
    public ProxyListenerDiff(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Changed, IReadOnlyList<string> Unchanged)
    {
        this.Added = ProxyListenerList.Copy(Added);
        this.Removed = ProxyListenerList.Copy(Removed);
        this.Changed = ProxyListenerList.Copy(Changed);
        this.Unchanged = ProxyListenerList.Copy(Unchanged);
    }

    public IReadOnlyList<string> Added { get; }
    public IReadOnlyList<string> Removed { get; }
    public IReadOnlyList<string> Changed { get; }
    public IReadOnlyList<string> Unchanged { get; }
}
