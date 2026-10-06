namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeRouteFacts
{
    public static void Validate(string name, string host, string pathPrefix, RuntimeRouteAction action, string loadBalancingPolicy, string siteName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(loadBalancingPolicy);
        ArgumentNullException.ThrowIfNull(siteName);
        if (!pathPrefix.StartsWith('/'))
        {
            throw new ArgumentException("Route path prefix must start with '/'.", nameof(pathPrefix));
        }

        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }
}
