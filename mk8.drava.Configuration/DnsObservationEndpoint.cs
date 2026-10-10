using System.Net;
using System.Net.Sockets;

namespace Mk8.Drava.Configuration;

public static class DnsObservationEndpoint
{
    public static IPEndPoint Parse(string address, int port)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (port is < 1 or > 65535 || !IPAddress.TryParse(address, out var parsed) ||
            parsed.Equals(IPAddress.Any) || parsed.Equals(IPAddress.IPv6Any) || parsed.IsIPv4MappedToIPv6 || parsed.IsIPv6Multicast ||
            (parsed.AddressFamily == AddressFamily.InterNetworkV6 && parsed.ScopeId != 0) ||
            (parsed.AddressFamily == AddressFamily.InterNetwork && parsed.GetAddressBytes()[0] is 0 or >= 224))
        {
            throw new InvalidDataException("DNS observation requires an explicitly configured unicast resolver literal and port.");
        }
        return new IPEndPoint(parsed, port);
    }
}
