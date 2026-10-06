namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeUpstreamFacts
{
    public static void Validate(string routeName, string name, string scheme, string protocol, string address, int port, int weight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        if (!IsSupportedScheme(scheme))
        {
            throw new ArgumentException("Upstream scheme must be 'http' or 'https'.", nameof(scheme));
        }

        if (!IsSupportedProtocol(protocol))
        {
            throw new ArgumentException("Upstream protocol must be 'http1', 'http2', or 'http3'.", nameof(protocol));
        }

        if ((RuntimeUpstreamProtocol.IsHttp2(protocol) || RuntimeUpstreamProtocol.IsHttp3(protocol)) && !string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("HTTP/2 and HTTP/3 upstreams require scheme 'https'.", nameof(protocol));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (weight is < 1 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(weight));
        }
    }

    public static void ValidateProjection(string routeName, string name, string scheme, string protocol, string address, int port, int weight, string endpoint, string uriEndpoint, string effectiveSniHost, string identity)
    {
        Validate(routeName, name, scheme, protocol, address, port, weight);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(uriEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectiveSniHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
    }

    private static bool IsSupportedScheme(string scheme)
    {
        return string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) || string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedProtocol(string protocol)
    {
        return string.Equals(protocol, RuntimeUpstreamProtocol.Http1, StringComparison.OrdinalIgnoreCase) || RuntimeUpstreamProtocol.IsHttp2(protocol) || RuntimeUpstreamProtocol.IsHttp3(protocol);
    }
}
