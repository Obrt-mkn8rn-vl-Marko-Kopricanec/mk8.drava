namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ConfigLintRouteOrderingAnalyzer
{
    public static IReadOnlyList<ConfigLintFinding> Analyze(ProxyConfigLintConfigurationSnapshot snapshot, string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        List<ConfigLintFinding> findings = [];
        AddOrderingFindings(snapshot, sourceName, findings);
        AddIdentityFindings(snapshot, sourceName, findings);
        return findings;
    }

    private static void AddOrderingFindings(ProxyConfigLintConfigurationSnapshot snapshot, string? sourceName, List<ConfigLintFinding> findings)
    {
        for (var laterIndex = 0; laterIndex < snapshot.Routes.Count; laterIndex++)
        {
            var later = snapshot.Routes[laterIndex];
            var shadowReported = false;
            var broadCatchAllReported = false;
            for (var earlierIndex = 0; earlierIndex < laterIndex; earlierIndex++)
            {
                var earlier = snapshot.Routes[earlierIndex];
                if (!shadowReported && RouteShadows(earlier, later))
                {
                    findings.Add(ConfigLintFindingFactory.Warning("route_shadowed", $"Route '{later.Name}' is shadowed by earlier route '{earlier.Name}'.", sourceName, ConfigLintRouteIdentityPolicy.RoutePath(later), "Move the more specific route before the broad route or narrow the earlier path prefix."));
                    shadowReported = true;
                }

                if (!broadCatchAllReported && IsBroadCatchAll(earlier) && ConfigLintRouteIdentityPolicy.HostOverlaps(earlier.Host, later.Host))
                {
                    findings.Add(ConfigLintFindingFactory.Warning("broad_catch_all_before_specific", $"Catch-all route '{earlier.Name}' appears before more specific route '{later.Name}'.", sourceName, ConfigLintRouteIdentityPolicy.RoutePath(earlier), "Put catch-all routes last."));
                    broadCatchAllReported = true;
                }

                if (shadowReported && broadCatchAllReported)
                {
                    break;
                }
            }
        }
    }

    private static void AddIdentityFindings(ProxyConfigLintConfigurationSnapshot snapshot, string? sourceName, List<ConfigLintFinding> findings)
    {
        foreach (var group in snapshot.Routes.GroupBy(static route => $"{route.Host}|{route.PathPrefix}", StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                findings.Add(ConfigLintFindingFactory.Warning("overlapping_route_identity", $"Multiple routes use host/path identity '{group.Key}'.", sourceName, "routes", "Keep one route per host and path prefix or make ordering intentional."));
            }
        }
    }

    private static bool RouteShadows(ProxyConfigLintRoute earlier, ProxyConfigLintRoute later)
    {
        return ConfigLintRouteIdentityPolicy.HostOverlaps(earlier.Host, later.Host) && later.PathPrefix.StartsWith(earlier.PathPrefix, StringComparison.Ordinal);
    }

    private static bool IsBroadCatchAll(ProxyConfigLintRoute route)
    {
        return string.Equals(route.Host, "*", StringComparison.Ordinal) && string.Equals(route.PathPrefix, "/", StringComparison.Ordinal);
    }
}
