namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record ProxyRouteDryRunFailureSnapshot
{
    public ProxyRouteDryRunFailureSnapshot(string Reason, long Count)
    {
        ArgumentNullException.ThrowIfNull(Reason);
        ArgumentOutOfRangeException.ThrowIfLessThan(Count, 0);

        this.Reason = Reason;
        this.Count = Count;
    }

    public string Reason { get; }
    public long Count { get; }
}
