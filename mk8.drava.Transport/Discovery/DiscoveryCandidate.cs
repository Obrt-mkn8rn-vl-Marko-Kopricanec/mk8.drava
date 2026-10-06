using System.Net;
using System.Net.Sockets;

namespace Mk8.Drava.Transport.Discovery;

// A locator is untrusted until the enrolled site TLS identity and fresh registration challenge succeed.
public sealed record DiscoveryCandidate
{
    public DiscoveryCandidate(string address, int port)
    {
        if (!IPAddress.TryParse(address, out var parsed) || !string.Equals(address, parsed.ToString(), StringComparison.Ordinal) ||
            parsed.Equals(IPAddress.Any) || parsed.Equals(IPAddress.IPv6Any) || parsed.IsIPv6Multicast || parsed.IsIPv4MappedToIPv6 || port is < 1 or > 65535 ||
            (parsed.AddressFamily == AddressFamily.InterNetwork && parsed.GetAddressBytes()[0] is 0 or >= 224)) throw new InvalidDataException("Discovery requires a canonical unicast literal and port.");
        Address = address;
        Port = port;
    }
    public string Address { get; }
    public int Port { get; }
}
