namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
internal static class ConfigLintRouteIdentityPolicy
{
    public static bool HostOverlaps(string earlierHost, string laterHost)
    {
        return string.Equals(earlierHost, "*", StringComparison.Ordinal) || string.Equals(laterHost, "*", StringComparison.Ordinal) || HostEquals(earlierHost, laterHost);
    }

    public static bool HostEquals(string left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(StripPort(left), StripPort(right), StringComparison.OrdinalIgnoreCase);
    }

    public static string RoutePath(ProxyConfigLintRoute route)
    {
        return $"sites[{route.SiteName}].routes[{route.Name}]";
    }

    private static string StripPort(string host)
    {
        var colonIndex = host.LastIndexOf(':');
        return colonIndex <= 0 || host.Contains(']', StringComparison.Ordinal) ? host : host[..colonIndex];
    }
}
