namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ConfigLintRouteCanonicalHostAnalyzer
{
    public static IReadOnlyList<ConfigLintFinding> Analyze(ProxyConfigLintRoute route, string routePath, string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!route.CanonicalHostEnabled || !ConfigLintRouteIdentityPolicy.HostEquals(route.Host, route.CanonicalHostTargetHost))
        {
            return[];
        }

        return[ConfigLintFindingFactory.Warning("canonical_host_loop", $"Route '{route.Name}' canonical host target equals its configured host.", sourceName, routePath, "Remove the canonical host policy or set a different target host.")];
    }
}
