using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Mk8.Drava.IntegrationTests;

internal static class DevelopmentPortAllocator
{
    private static readonly System.Threading.Lock Gate = new();
    private static readonly HashSet<int> Issued = [];
    private static readonly (int First, int Last) AutomaticRange = ReadAutomaticRange();

    public static int GetPort()
    {
        if (!OperatingSystem.IsLinux())
        {
            using var automatic = new TcpListener(IPAddress.Loopback, 0);
            automatic.Start();
            return ((IPEndPoint)automatic.LocalEndpoint).Port;
        }
        lock (Gate)
        {
            for (var attempt = 0; attempt < 65536; attempt++)
            {
                var port = RandomNumberGenerator.GetInt32(1024, 65536);
                if (port >= AutomaticRange.First && port <= AutomaticRange.Last || Issued.Contains(port)) continue;
                using var listener = new TcpListener(IPAddress.Loopback, port);
                try { listener.Start(); }
                catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse) { continue; }
                Issued.Add(port);
                return port;
            }
        }
        throw new IOException("No unique development listener port is available outside the automatic local range.");
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
