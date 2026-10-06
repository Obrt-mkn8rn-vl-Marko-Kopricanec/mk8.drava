namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyCacheSubsystemSummary
{
    public ProxyCacheSubsystemSummary(bool Enabled, int EnabledRoutes, int EntryCount, long ApproximateBytes)
    {
        ProxyStatusFacts.RequireNonNegative(EnabledRoutes, nameof(EnabledRoutes));
        ProxyStatusFacts.RequireNonNegative(EntryCount, nameof(EntryCount));
        ProxyStatusFacts.RequireNonNegative(ApproximateBytes, nameof(ApproximateBytes));
        this.Enabled = Enabled;
        this.EnabledRoutes = EnabledRoutes;
        this.EntryCount = EntryCount;
        this.ApproximateBytes = ApproximateBytes;
    }

    public bool Enabled { get; }
    public int EnabledRoutes { get; }
    public int EntryCount { get; }
    public long ApproximateBytes { get; }
    public static ProxyCacheSubsystemSummary Unknown { get; } = new(false, 0, 0, 0);
}
