using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Mk8.Drava.CompatibilityTests;
internal static class TestPortAllocator
{
    private static readonly ConcurrentDictionary<int, byte> AllocatedPorts = new();
    private static readonly (int First, int Last) AutomaticRange = ReadAutomaticRange();

    public static int GetFreeTcpPort()
    {
        if (OperatingSystem.IsLinux()) return GetDeclaredPort(tcpRequired: true, udpRequired: false);
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Server.ExclusiveAddressUse = true;
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            if (AllocatedPorts.TryAdd(port, 0)) return port;
        }
        throw new InvalidOperationException("Could not allocate a unique TCP test port.");
    }

    public static int GetFreeUdpPort()
    {
        if (OperatingSystem.IsLinux()) return GetDeclaredPort(tcpRequired: false, udpRequired: true);
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udp.ExclusiveAddressUse = true;
            udp.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var port = ((IPEndPoint)udp.LocalEndPoint!).Port;
            if (AllocatedPorts.TryAdd(port, 0)) return port;
        }
        throw new InvalidOperationException("Could not allocate a unique UDP test port.");
    }

    public static int GetFreeTcpUdpPort()
    {
        if (OperatingSystem.IsLinux()) return GetDeclaredPort(tcpRequired: true, udpRequired: true);
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Server.ExclusiveAddressUse = true;
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            try
            {
                if (AllocatedPorts.ContainsKey(port)) continue;
                using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                udp.ExclusiveAddressUse = true;
                udp.Bind(new IPEndPoint(IPAddress.Loopback, port));
                if (AllocatedPorts.TryAdd(port, 0)) return port;
            }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse) { }
        }
        throw new InvalidOperationException("Could not allocate a unique TCP/UDP test port pair.");
    }

    private static int GetDeclaredPort(bool tcpRequired, bool udpRequired)
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var port = RandomNumberGenerator.GetInt32(1024, 65536);
            if (port >= AutomaticRange.First && port <= AutomaticRange.Last || AllocatedPorts.ContainsKey(port)) continue;
            using var listener = tcpRequired ? new TcpListener(IPAddress.Loopback, port) : null;
            using var udp = udpRequired ? new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) : null;
            try
            {
                if (listener is not null)
                {
                    listener.Server.ExclusiveAddressUse = true;
                    listener.Start();
                }
                if (udp is not null)
                {
                    udp.ExclusiveAddressUse = true;
                    udp.Bind(new IPEndPoint(IPAddress.Loopback, port));
                }
                if (AllocatedPorts.TryAdd(port, 0)) return port;
            }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse) { }
        }
        throw new InvalidOperationException("Could not allocate a unique declared listener port outside automatic client ports.");
    }

    private static (int First, int Last) ReadAutomaticRange()
    {
        if (!OperatingSystem.IsLinux()) return (0, 0);
        var values = File.ReadAllText("/proc/sys/net/ipv4/ip_local_port_range").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (values.Length != 2 || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first)
            || !int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out var last)
            || first is < 1 or > 65535 || last < first || last > 65535)
            throw new InvalidDataException("Invalid development host automatic local-port range.");
        return (first, last);
    }
}
