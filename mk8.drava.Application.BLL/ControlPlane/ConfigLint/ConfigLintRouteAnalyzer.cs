namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ConfigLintRouteAnalyzer
{
    public static IReadOnlyList<ConfigLintFinding> Analyze(ProxyConfigLintConfigurationSnapshot snapshot, string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        List<ConfigLintFinding> findings = [];
        AddPerRouteFindings(snapshot, sourceName, findings);
        return findings;
    }

    private static void AddPerRouteFindings(ProxyConfigLintConfigurationSnapshot snapshot, string? sourceName, List<ConfigLintFinding> findings)
    {
        foreach (var route in snapshot.Routes)
        {
            var routePath = ConfigLintRouteIdentityPolicy.RoutePath(route);
            findings.AddRange(ConfigLintRouteCanonicalHostAnalyzer.Analyze(route, routePath, sourceName));
            findings.AddRange(ConfigLintRouteCacheAnalyzer.Analyze(route, routePath, sourceName));
            findings.AddRange(ConfigLintRouteResilienceAnalyzer.Analyze(route, routePath, sourceName));
            findings.AddRange(ConfigLintUpstreamAnalyzer.Analyze(snapshot, route, routePath, sourceName));
            findings.AddRange(ConfigLintRouteGeneratedResponseAnalyzer.Analyze(route, routePath, sourceName));
        }
    }
}
