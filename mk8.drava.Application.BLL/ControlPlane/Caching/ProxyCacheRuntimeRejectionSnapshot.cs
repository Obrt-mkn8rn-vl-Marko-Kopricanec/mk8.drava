namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed record ProxyCacheRuntimeRejectionSnapshot
{
    public ProxyCacheRuntimeRejectionSnapshot(string Reason, long Count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Reason);
        ArgumentOutOfRangeException.ThrowIfNegative(Count);
        this.Reason = Reason;
        this.Count = Count;
    }

    public string Reason { get; }
    public long Count { get; }
}
