namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed record ProxyCacheRejectionStatus
{
    private ProxyCacheRejectionStatus(string reason, long count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Reason = reason;
        Count = count;
    }

    public string Reason { get; }
    public long Count { get; }

    public static ProxyCacheRejectionStatus FromSources(string reason, long count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new ProxyCacheRejectionStatus(reason, count);
    }
}
