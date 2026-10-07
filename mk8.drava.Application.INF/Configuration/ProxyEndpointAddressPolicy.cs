using System.Net;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.INF.Configuration;
public sealed class ProxyEndpointAddressPolicy : IProxyEndpointAddressPolicy
{
    public bool IsListenerAddress(string value)
    {
        return IPAddress.TryParse(value, out _);
    }

    public bool IsAmbiguousUpstreamAddress(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var address = value.Trim();
        if (address.Length == 0 || address.Contains("://", StringComparison.Ordinal) || address.Contains('/', StringComparison.Ordinal) || address.Contains('\\', StringComparison.Ordinal) || address.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            return true;
        }

        if (IPAddress.TryParse(address, out _))
        {
            return false;
        }

        return address.Contains(':', StringComparison.Ordinal);
    }

    public bool IsValidSniHost(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var host = value.Trim();
        if (host.Length is 0 or > 253 || host.StartsWith("*.", StringComparison.Ordinal) || host.Contains('/', StringComparison.Ordinal) || host.Contains('\\', StringComparison.Ordinal) || host.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            return false;
        }

        if (IPAddress.TryParse(host, out _))
        {
            return true;
        }

        return !host.Contains(':', StringComparison.Ordinal) && Uri.CheckHostName(host)is UriHostNameType.Dns or UriHostNameType.IPv4;
    }
}
