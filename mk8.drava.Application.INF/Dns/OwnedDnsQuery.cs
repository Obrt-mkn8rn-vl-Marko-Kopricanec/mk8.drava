using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Mk8.Drava.Application.INF.Dns;

internal static class OwnedDnsQuery
{
    public static async ValueTask<byte[]> QueryAsync(IPEndPoint resolver, byte[] query, ushort transactionId, CancellationToken cancellationToken)
    {
        byte[] response;
        using (var udp = new Socket(resolver.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
        {
            await udp.ConnectAsync(resolver, cancellationToken).ConfigureAwait(false);
            if (await udp.SendAsync(query, SocketFlags.None, cancellationToken).ConfigureAwait(false) != query.Length)
                throw new IOException("DNS datagram send was incomplete.");
            var buffer = new byte[65535];
            var count = await udp.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
            response = buffer.AsSpan(0, count).ToArray();
        }
        if (!DnsWire.IsTruncated(response, transactionId)) return response;
        using var tcp = new TcpClient(resolver.AddressFamily);
        await tcp.ConnectAsync(resolver, cancellationToken).ConfigureAwait(false);
        var stream = tcp.GetStream();
        await using var streamLifetime = stream.ConfigureAwait(false);
        var prefix = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(prefix, checked((ushort)query.Length));
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(query, cancellationToken).ConfigureAwait(false);
        await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt16BigEndian(prefix);
        if (length < 12) throw new InvalidDataException("DNS TCP response is shorter than its header.");
        response = new byte[length];
        await stream.ReadExactlyAsync(response, cancellationToken).ConfigureAwait(false);
        return response;
    }
}
