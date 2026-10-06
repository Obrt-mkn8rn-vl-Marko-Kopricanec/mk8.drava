namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ConfigLintSiteAnalyzer
{
    public static IReadOnlyList<ConfigLintFinding> Analyze(ProxyConfigLintConfigurationSnapshot snapshot, string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        List<ConfigLintFinding> findings = [];
        foreach (var group in snapshot.Routes.GroupBy(static route => route.SiteName, StringComparer.OrdinalIgnoreCase))
        {
            if (!group.Any(static route => route.PathPrefix == "/"))
            {
                findings.Add(ConfigLintFindingFactory.Info("site_without_fallback_route", $"Site '{group.Key}' has no '/' fallback route.", sourceName, $"sites[{group.Key}]", "Add an explicit fallback route if unmatched paths should have controlled behavior."));
            }
        }

        return findings;
    }
}
