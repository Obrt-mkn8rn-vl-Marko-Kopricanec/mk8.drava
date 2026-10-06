using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Haukcode.Mdns;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Transport.Discovery;

public static class SiteDiscovery
{
    public static async ValueTask<IReadOnlyList<DiscoveryCandidate>> FindAsync(string siteId, CancellationToken cancellationToken)
    {
        RegistrationSiteTrust.RequireLabel(siteId);
        var candidates = new Dictionary<string, DiscoveryCandidate>(StringComparer.Ordinal);
        var networks = LocalDiscoveryNetwork.Read();
        foreach (var network in networks)
        {
            if (candidates.Count >= 16) break;
            var found = await QueryAsync(siteId, network, cancellationToken).ConfigureAwait(false);
            foreach (var candidate in found)
            {
                if (candidates.Count == 16) break;
                candidates.TryAdd(candidate.Address + ":" + candidate.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), candidate);
            }
        }
        return Array.AsReadOnly(candidates.Values.ToArray());
    }

    private static async ValueTask<IReadOnlyList<DiscoveryCandidate>> QueryAsync(string siteId, LocalDiscoveryNetwork network, CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(1));
        var queryId = (ushort)RandomNumberGenerator.GetInt32(1, ushort.MaxValue);
        var query = new DnsMessage { Id = queryId };
        query.Questions.Add(new DnsQuestion(MdnsCandidateReader.ServiceType, DnsRecordType.PTR, DnsClass.IN));
        var reader = new MdnsCandidateReader(siteId, network);
        var buffer = new byte[9000];
        try
        {
            socket.Bind(new IPEndPoint(network.Address, 0));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, network.Address.GetAddressBytes());
            // The local responder is also queried directly: loopback interfaces do not join mDNS,
            // and host firewalls may block multicast even between processes on the same machine.
            // Both queries request legacy unicast replies on this short-lived socket.
            var encoded = DnsEncoder.Encode(query);
            await socket.SendToAsync(encoded, SocketFlags.None, new IPEndPoint(network.Address, 5353), deadline.Token).ConfigureAwait(false);
            if (!IPAddress.IsLoopback(network.Address))
                await socket.SendToAsync(encoded, SocketFlags.None, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353), deadline.Token).ConfigureAwait(false);
            for (var packet = 0; packet < 64; packet++)
            {
                var response = await socket.ReceiveMessageFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), deadline.Token).ConfigureAwait(false);
                if ((response.SocketFlags & SocketFlags.Truncated) != SocketFlags.None || response.RemoteEndPoint is not IPEndPoint peer || peer.Port != 5353 || !network.Contains(peer.Address)) continue;
                var found = reader.Read(buffer.AsSpan(0, response.ReceivedBytes).ToArray(), queryId, peer.Address.Equals(network.Address));
                if (found.Count > 0) return found;
            }
        }
        catch (SocketException) { }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        return [];
    }
}
