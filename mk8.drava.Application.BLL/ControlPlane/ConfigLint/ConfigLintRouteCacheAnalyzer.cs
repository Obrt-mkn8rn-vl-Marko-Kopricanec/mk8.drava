namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ConfigLintRouteCacheAnalyzer
{
    public static IReadOnlyList<ConfigLintFinding> Analyze(ProxyConfigLintRoute route, string routePath, string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!route.CacheEnabled || !LooksPrivate(route))
        {
            return[];
        }

        return[ConfigLintFindingFactory.Warning("cache_private_path", $"Route '{route.Name}' enables cache on a path or header pattern that commonly serves private content.", sourceName, routePath, "Keep caching disabled for authenticated or user-specific resources.")];
    }

    private static bool LooksPrivate(ProxyConfigLintRoute route)
    {
        var path = route.PathPrefix;
        return path.Contains("admin", StringComparison.OrdinalIgnoreCase) || path.Contains("auth", StringComparison.OrdinalIgnoreCase) || path.Contains("account", StringComparison.OrdinalIgnoreCase) || path.Contains("private", StringComparison.OrdinalIgnoreCase) || path.Contains("profile", StringComparison.OrdinalIgnoreCase) || path.Contains("user", StringComparison.OrdinalIgnoreCase) || route.CacheVaryByHeaders.Any(static header => string.Equals(header, "Authorization", StringComparison.OrdinalIgnoreCase) || string.Equals(header, "Cookie", StringComparison.OrdinalIgnoreCase));
    }
}
