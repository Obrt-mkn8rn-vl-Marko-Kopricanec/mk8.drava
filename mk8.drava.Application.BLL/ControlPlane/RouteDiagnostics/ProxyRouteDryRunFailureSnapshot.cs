namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record ProxyRouteDryRunFailureSnapshot
{
    public ProxyRouteDryRunFailureSnapshot(string Reason, long Count)
    {
        ArgumentNullException.ThrowIfNull(Reason);
        if (Count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Count));
        }

        this.Reason = Reason;
        this.Count = Count;
    }

    public string Reason { get; }
    public long Count { get; }
}
