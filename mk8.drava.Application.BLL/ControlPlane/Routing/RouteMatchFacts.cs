namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
internal static class RouteMatchFacts
{
    public static void ValidateRouteIndex(int routeIndex)
    {
        if (routeIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routeIndex));
        }
    }

    public static void ValidateCandidate(string host, string pathPrefix)
    {
        ValidateHost(host, nameof(host));
        ValidatePath(pathPrefix, nameof(pathPrefix));
    }

    public static void ValidateRequest(string host, string path)
    {
        ValidateHost(host, nameof(host));
        ValidatePath(path, nameof(path));
    }

    private static void ValidateHost(string host, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Route match host is required.", parameterName);
        }
    }

    private static void ValidatePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Route match path is required.", parameterName);
        }

        if (!path.StartsWith('/'))
        {
            throw new ArgumentException("Route match path must start with '/'.", parameterName);
        }
    }
}
