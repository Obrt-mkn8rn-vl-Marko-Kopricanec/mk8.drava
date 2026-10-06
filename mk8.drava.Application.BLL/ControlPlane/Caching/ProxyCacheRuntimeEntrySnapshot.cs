namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed record ProxyCacheRuntimeEntrySnapshot
{
    public ProxyCacheRuntimeEntrySnapshot(string RouteName, long SizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RouteName);
        ArgumentOutOfRangeException.ThrowIfNegative(SizeBytes);
        this.RouteName = RouteName;
        this.SizeBytes = SizeBytes;
    }

    public string RouteName { get; }
    public long SizeBytes { get; }
}
