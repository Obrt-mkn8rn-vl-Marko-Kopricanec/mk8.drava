using System.Buffers.Binary;
using Mk8.Drava.Application.INF.Dns;

namespace Mk8.Drava.Application.INF.Acme;

internal static class AcmeDns01WireResponse
{
    public static byte[] CreateQuery(string host, ushort transactionId) => DnsWire.CreateQuery(host, transactionId, 16);
    public static bool IsTruncated(ReadOnlySpan<byte> packet, ushort transactionId) => DnsWire.IsTruncated(packet, transactionId);

    public static bool HasProof(ReadOnlySpan<byte> packet, ushort transactionId, string host, string value)
    {
        DnsWire.RequireHeader(packet, transactionId);
        var flags = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        if ((flags & 0x020f) != 0) return false;
        var answers = BinaryPrimitives.ReadUInt16BigEndian(packet[6..]);
        var authorities = BinaryPrimitives.ReadUInt16BigEndian(packet[8..]);
        var additional = BinaryPrimitives.ReadUInt16BigEndian(packet[10..]);
        if (answers > 128 || authorities + additional > 128) throw new InvalidDataException("DNS01 response exceeds its record bound.");
        var offset = 12;
        if (!string.Equals(DnsWire.ReadName(packet, ref offset), host, StringComparison.OrdinalIgnoreCase) || DnsWire.Read16(packet, ref offset) != 16 || DnsWire.Read16(packet, ref offset) != 1)
            throw new InvalidDataException("DNS01 response changes its requested question.");
        var found = false;
        for (var i = 0; i < answers + authorities + additional; i++)
        {
            var owner = DnsWire.ReadName(packet, ref offset);
            var type = DnsWire.Read16(packet, ref offset); var queryClass = DnsWire.Read16(packet, ref offset);
            DnsWire.RequireAvailable(packet, offset, 4); offset += 4;
            var length = DnsWire.Read16(packet, ref offset);
            DnsWire.RequireAvailable(packet, offset, length);
            if (i < answers)
            {
                if (type != 16 || queryClass != 1 || !string.Equals(owner, host, StringComparison.OrdinalIgnoreCase)) return false;
                found |= MatchesTxt(packet.Slice(offset, length), value);
            }
            offset += length;
        }
        if (offset != packet.Length) throw new InvalidDataException("DNS01 response has trailing unframed bytes.");
        return found;
    }

    private static bool MatchesTxt(ReadOnlySpan<byte> data, string value)
    {
        var offset = 0; var valueOffset = 0; var equal = true;
        while (offset < data.Length)
        {
            var length = data[offset++];
            DnsWire.RequireAvailable(data, offset, length);
            for (var i = 0; i < length; i++)
            {
                if (valueOffset >= value.Length || data[offset + i] != value[valueOffset]) equal = false;
                valueOffset++;
            }
            offset += length;
        }
        return equal && valueOffset == value.Length;
    }

}
