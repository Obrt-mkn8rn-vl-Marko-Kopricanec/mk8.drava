namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public sealed class SingleUpstreamRouteMatcher : IRouteMatcher
{
    public RouteMatch? Match(IReadOnlyList<RouteMatchCandidate> routes, RouteMatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(routes);
        for (var index = 0; index < routes.Count; index++)
        {
            var route = routes[index];
            if (!HostMatches(route.Host, request.Host))
            {
                continue;
            }

            if (!request.Path.StartsWith(route.PathPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            return new RouteMatch(index);
        }

        return null;
    }

    private static bool HostMatches(string configuredHost, string requestHost)
    {
        if (string.Equals(configuredHost, "*", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(configuredHost, requestHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var requestHostWithoutPort = StripSimplePort(requestHost);
        return string.Equals(configuredHost, requestHostWithoutPort, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripSimplePort(string host)
    {
        var colonIndex = host.LastIndexOf(':');
        if (colonIndex <= 0 || host.Contains(']', StringComparison.Ordinal))
        {
            return host;
        }

        return host[..colonIndex];
    }
}
