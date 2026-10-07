using System.Net;

namespace Mk8.Drava.Application.BLL.Registry;

// Immutable route identity carried by the same proof that establishes publication eligibility.
public sealed record PublishedServiceAddress
{
    public PublishedServiceAddress(string host, string pathPrefix)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(pathPrefix);
        if (host.Length is < 3 or > 253 || !host.Contains('.', StringComparison.Ordinal) || IPAddress.TryParse(host, out _))
            throw new InvalidDataException("Automatic routes require an explicit ASCII DNS hostname.");
        foreach (var label in host.Split('.'))
        {
            if (label.Length == 0) throw new InvalidDataException("Automatic route hostnames cannot contain empty labels.");
            RegistryNames.RequireLabel(label);
        }
        if (pathPrefix.Length is < 1 or > 2048 || !pathPrefix.StartsWith('/') || pathPrefix.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException("Automatic route paths require a bounded absolute path.");
        foreach (var character in pathPrefix)
            if (character is < '!' or > '~' or '\\' or '?' or '#')
                throw new InvalidDataException("Automatic route paths require escaped ASCII without query or fragment.");
        if (!Uri.TryCreate("http://route.invalid" + pathPrefix, UriKind.Absolute, out var address) ||
            !string.Equals(address.AbsolutePath, pathPrefix, StringComparison.Ordinal))
            throw new InvalidDataException("Automatic route paths must use canonical URI escaping without dot segments.");
        if (Within(pathPrefix, "/admin") || Within(pathPrefix, "/_drava") || Within(pathPrefix, "/mk8.drava.proxy.v1.ServiceRegistry"))
            throw new InvalidDataException("Automatic route paths cannot use a reserved Gateway presentation namespace.");
        Host = host;
        PathPrefix = pathPrefix;
    }

    public string Host { get; }
    public string PathPrefix { get; }

    private static bool Within(string path, string root) => string.Equals(path, root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
}
