using System.Buffers.Binary;
using System.Text;

namespace Mk8.Drava.Application.INF.Acme;

internal static class AcmeDns01WireResponse
{
    public static byte[] CreateQuery(string host, ushort transactionId)
    {
        var bytes = new List<byte>(512);
        bytes.Add((byte)(transactionId >> 8)); bytes.Add((byte)transactionId);
        bytes.AddRange([1, 0, 0, 1, 0, 0, 0, 0, 0, 0]);
        foreach (var label in host.Split('.'))
        {
            if (label.Length is < 1 or > 63 || label.Any(static c => !char.IsAscii(c))) throw new InvalidDataException("DNS01 query has an invalid ASCII owner label.");
            bytes.Add(checked((byte)label.Length)); bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }
        bytes.AddRange([0, 0, 16, 0, 1]);
        if (bytes.Count > 512) throw new InvalidDataException("DNS01 query exceeds its wire bound.");
        return bytes.ToArray();
    }

    public static bool IsTruncated(ReadOnlySpan<byte> packet, ushort transactionId)
    {
        RequireHeader(packet, transactionId);
        return (BinaryPrimitives.ReadUInt16BigEndian(packet[2..]) & 0x0200) != 0;
    }

    public static bool HasProof(ReadOnlySpan<byte> packet, ushort transactionId, string host, string value)
    {
        RequireHeader(packet, transactionId);
        var flags = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        if ((flags & 0x020f) != 0) return false;
        var answers = BinaryPrimitives.ReadUInt16BigEndian(packet[6..]);
        var authorities = BinaryPrimitives.ReadUInt16BigEndian(packet[8..]);
        var additional = BinaryPrimitives.ReadUInt16BigEndian(packet[10..]);
        if (answers > 128 || authorities + additional > 128) throw new InvalidDataException("DNS01 response exceeds its record bound.");
        var offset = 12;
        if (!string.Equals(ReadName(packet, ref offset), host, StringComparison.OrdinalIgnoreCase) || Read16(packet, ref offset) != 16 || Read16(packet, ref offset) != 1)
            throw new InvalidDataException("DNS01 response changes its requested question.");
        var found = false;
        for (var i = 0; i < answers + authorities + additional; i++)
        {
            var owner = ReadName(packet, ref offset);
            var type = Read16(packet, ref offset); var queryClass = Read16(packet, ref offset);
            RequireAvailable(packet, offset, 4); offset += 4;
            var length = Read16(packet, ref offset);
            RequireAvailable(packet, offset, length);
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
            RequireAvailable(data, offset, length);
            for (var i = 0; i < length; i++)
            {
                if (valueOffset >= value.Length || data[offset + i] != value[valueOffset]) equal = false;
                valueOffset++;
            }
            offset += length;
        }
        return equal && valueOffset == value.Length;
    }

    private static string ReadName(ReadOnlySpan<byte> packet, ref int offset)
    {
        var labels = new List<string>(8);
        var position = offset; var jumped = false; var wireLength = 1;
        for (var hops = 0; hops < 128; hops++)
        {
            RequireAvailable(packet, position, 1);
            var length = packet[position++];
            if (length == 0)
            {
                if (!jumped) offset = position;
                return string.Join('.', labels);
            }
            if ((length & 0xc0) == 0xc0)
            {
                RequireAvailable(packet, position, 1);
                var target = ((length & 0x3f) << 8) | packet[position++];
                if (target >= position - 2 || target < 12) throw new InvalidDataException("DNS01 response contains a cyclic or forward name pointer.");
                if (!jumped) offset = position;
                jumped = true; position = target; continue;
            }
            if ((length & 0xc0) != 0 || wireLength + length + 1 > 255) throw new InvalidDataException("DNS01 owner name exceeds its wire bound.");
            RequireAvailable(packet, position, length);
            var label = packet.Slice(position, length);
            foreach (ref readonly var character in label)
                if (character > 127 || character <= 32 || character == (byte)'.') throw new InvalidDataException("DNS01 owner name contains an invalid ASCII label.");
            labels.Add(Encoding.ASCII.GetString(label)); wireLength += length + 1; position += length;
        }
        throw new InvalidDataException("DNS01 name pointers exceed their traversal bound.");
    }

    private static ushort Read16(ReadOnlySpan<byte> packet, ref int offset)
    {
        RequireAvailable(packet, offset, 2);
        var result = BinaryPrimitives.ReadUInt16BigEndian(packet[offset..]); offset += 2; return result;
    }
    private static void RequireHeader(ReadOnlySpan<byte> packet, ushort transactionId)
    {
        if (packet.Length is < 12 or > 65535 || BinaryPrimitives.ReadUInt16BigEndian(packet) != transactionId ||
            (BinaryPrimitives.ReadUInt16BigEndian(packet[2..]) & 0xf800) != 0x8000 || BinaryPrimitives.ReadUInt16BigEndian(packet[4..]) != 1)
            throw new InvalidDataException("DNS01 response has an invalid transaction, header or question count.");
    }
    private static void RequireAvailable(ReadOnlySpan<byte> packet, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > packet.Length - length) throw new InvalidDataException("DNS01 response is truncated.");
    }
}
