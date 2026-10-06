using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Mk8.Drava.Transport.Discovery;

internal sealed record LocalDiscoveryNetwork(IPAddress Address, int PrefixLength)
{
    public bool Contains(IPAddress address)
    {
        if (address.AddressFamily != Address.AddressFamily) return false;
        var left = Address.GetAddressBytes();
        var right = address.GetAddressBytes();
        for (var index = 0; index < left.Length; index++)
        {
            var bits = Math.Clamp(PrefixLength - index * 8, 0, 8);
            var mask = bits == 0 ? 0 : 255 << (8 - bits);
            if ((left[index] & mask) != (right[index] & mask)) return false;
        }
        return true;
    }

    public static IReadOnlyList<LocalDiscoveryNetwork> Read()
    {
        var networks = new List<LocalDiscoveryNetwork>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var address in adapter.GetIPProperties().UnicastAddresses)
            {
                if (networks.Count == 16) return networks;
                if (address.Address.AddressFamily != AddressFamily.InterNetwork || address.PrefixLength is < 1 or > 32) continue;
                networks.Add(new LocalDiscoveryNetwork(address.Address, address.PrefixLength));
            }
        }
        return networks;
    }
}
