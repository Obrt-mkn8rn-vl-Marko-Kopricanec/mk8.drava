namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyRouteSubsystemSummary
{
    public ProxyRouteSubsystemSummary(int Sites, int Routes, int ProxyRoutes, int GeneratedRoutes, int CacheEnabledRoutes)
    {
        ProxyStatusFacts.RequireNonNegative(Sites, nameof(Sites));
        ProxyStatusFacts.RequireNonNegative(Routes, nameof(Routes));
        ProxyStatusFacts.RequireNonNegative(ProxyRoutes, nameof(ProxyRoutes));
        ProxyStatusFacts.RequireNonNegative(GeneratedRoutes, nameof(GeneratedRoutes));
        ProxyStatusFacts.RequireNonNegative(CacheEnabledRoutes, nameof(CacheEnabledRoutes));
        this.Sites = Sites;
        this.Routes = Routes;
        this.ProxyRoutes = ProxyRoutes;
        this.GeneratedRoutes = GeneratedRoutes;
        this.CacheEnabledRoutes = CacheEnabledRoutes;
    }

    public int Sites { get; }
    public int Routes { get; }
    public int ProxyRoutes { get; }
    public int GeneratedRoutes { get; }
    public int CacheEnabledRoutes { get; }
    public static ProxyRouteSubsystemSummary Unknown { get; } = new(0, 0, 0, 0, 0);
}
