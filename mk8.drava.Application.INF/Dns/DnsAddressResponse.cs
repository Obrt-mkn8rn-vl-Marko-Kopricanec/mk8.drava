using System.Buffers.Binary;
using System.Net;

namespace Mk8.Drava.Application.INF.Dns;

internal static class DnsAddressResponse
{
    internal sealed record Answer(string Owner, IPAddress? Address, string? Alias, uint Ttl);
    internal sealed record Result(bool Valid, IReadOnlyList<Answer> Answers);

    public static Result Read(ReadOnlySpan<byte> packet, ushort transactionId, string host, ushort queryType)
    {
        DnsWire.RequireHeader(packet, transactionId);
        if (queryType is not (1 or 28)) throw new InvalidDataException("DNS address proof requires A or AAAA.");
        if ((BinaryPrimitives.ReadUInt16BigEndian(packet[2..]) & 0x020f) != 0) return new Result(false, []);
        var answers = BinaryPrimitives.ReadUInt16BigEndian(packet[6..]);
        var authorities = BinaryPrimitives.ReadUInt16BigEndian(packet[8..]); var additional = BinaryPrimitives.ReadUInt16BigEndian(packet[10..]);
        if (answers > 64 || authorities + additional > 128) throw new InvalidDataException("DNS address proof exceeds its record bound.");
        var offset = 12;
        if (!string.Equals(DnsWire.ReadName(packet, ref offset), host, StringComparison.OrdinalIgnoreCase) || DnsWire.Read16(packet, ref offset) != queryType || DnsWire.Read16(packet, ref offset) != 1)
            throw new InvalidDataException("DNS address response changes its requested question.");
        var result = new List<Answer>(answers);
        for (var i = 0; i < answers + authorities + additional; i++)
        {
            var owner = DnsWire.ReadName(packet, ref offset); var type = DnsWire.Read16(packet, ref offset); var queryClass = DnsWire.Read16(packet, ref offset);
            DnsWire.RequireAvailable(packet, offset, 4); var ttl = BinaryPrimitives.ReadUInt32BigEndian(packet[offset..]); offset += 4;
            var length = DnsWire.Read16(packet, ref offset); DnsWire.RequireAvailable(packet, offset, length);
            if (i < answers)
            {
                if (queryClass != 1) throw new InvalidDataException("DNS address answers require the Internet class.");
                if (type is 1 or 28)
                {
                    if (type != queryType || length != (type == 1 ? 4 : 16)) throw new InvalidDataException("DNS address has an unexpected type or length.");
                    result.Add(new Answer(owner, new IPAddress(packet.Slice(offset, length)), null, ttl));
                }
                else if (type == 5)
                {
                    var aliasOffset = offset; var alias = DnsWire.ReadName(packet, ref aliasOffset);
                    if (aliasOffset != offset + length) throw new InvalidDataException("DNS alias does not consume its exact record data.");
                    result.Add(new Answer(owner, null, alias, ttl));
                }
            }
            offset += length;
        }
        if (offset != packet.Length) throw new InvalidDataException("DNS address response has trailing unframed bytes.");
        return new Result(true, result.AsReadOnly());
    }
}
