namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed record ProxyCacheStatusRouteSource
{
    public ProxyCacheStatusRouteSource(string RouteName, bool Enabled, long MaxEntryBytes, long MaxTotalBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RouteName);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTotalBytes);
        this.RouteName = RouteName;
        this.Enabled = Enabled;
        this.MaxEntryBytes = MaxEntryBytes;
        this.MaxTotalBytes = MaxTotalBytes;
    }

    public string RouteName { get; }
    public bool Enabled { get; }
    public long MaxEntryBytes { get; }
    public long MaxTotalBytes { get; }
}
