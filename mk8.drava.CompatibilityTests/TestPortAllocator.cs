using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class TestPortAllocator
{
    private static readonly ConcurrentDictionary<int, byte> AllocatedPorts = new();
    public static int GetFreeTcpPort()
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Server.ExclusiveAddressUse = true;
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            try
            {
                if (AllocatedPorts.TryAdd(port, 0))
                {
                    return port;
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        throw new InvalidOperationException("Could not allocate a unique TCP test port.");
    }

    public static int GetFreeUdpPort()
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udp.ExclusiveAddressUse = true;
            udp.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var port = ((IPEndPoint)udp.LocalEndPoint!).Port;
            if (AllocatedPorts.TryAdd(port, 0))
            {
                return port;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique UDP test port.");
    }

    public static int GetFreeTcpUdpPort()
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Server.ExclusiveAddressUse = true;
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Socket? udp = null;
            try
            {
                if (AllocatedPorts.ContainsKey(port))
                {
                    continue;
                }

                udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                {
                    ExclusiveAddressUse = true
                };
                udp.Bind(new IPEndPoint(IPAddress.Loopback, port));
                if (AllocatedPorts.TryAdd(port, 0))
                {
                    return port;
                }
            }
            catch (SocketException)
            {
            }
            finally
            {
                udp?.Dispose();
                listener.Stop();
            }
        }

        throw new InvalidOperationException("Could not allocate a unique TCP/UDP test port pair.");
    }
}
