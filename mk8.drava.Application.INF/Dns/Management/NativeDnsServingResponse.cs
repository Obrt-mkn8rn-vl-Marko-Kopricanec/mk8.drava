using System.Buffers.Binary;
using Mk8.Drava.Application.INF.Dns;

namespace Mk8.Drava.Application.INF.Dns.Management;

internal static class NativeDnsServingResponse
{
    public static bool TryReadSerial(ReadOnlySpan<byte> packet, ushort transaction, string origin, out uint serial)
    {
        serial = 0;
        if (!ReadQuestion(packet, transaction, origin, 6, out var offset, out var answers, out var count)) return false;
        var found = false;
        for (var i = 0; i < count; i++)
        {
            var owner = DnsWire.ReadName(packet, ref offset); var type = DnsWire.Read16(packet, ref offset); var recordClass = DnsWire.Read16(packet, ref offset);
            DnsWire.RequireAvailable(packet, offset, 4); offset += 4;
            var length = DnsWire.Read16(packet, ref offset); DnsWire.RequireAvailable(packet, offset, length);
            var end = checked(offset + length);
            if (i < answers)
            {
                if (found || type != 6 || recordClass != 1 || !string.Equals(owner, origin, StringComparison.OrdinalIgnoreCase)) return false;
                var position = offset;
                _ = DnsWire.ReadName(packet, ref position); _ = DnsWire.ReadName(packet, ref position);
                DnsWire.RequireAvailable(packet, position, 20);
                if (position + 20 != end) throw new InvalidDataException("Authoritative SOA has invalid RDATA framing.");
                serial = BinaryPrimitives.ReadUInt32BigEndian(packet[position..]); found = true;
            }
            offset = end;
        }
        if (offset != packet.Length) throw new InvalidDataException("Authoritative SOA contains unframed bytes.");
        return found;
    }

    public static bool HasValues(ReadOnlySpan<byte> packet, ushort transaction, string owner, ushort type, IReadOnlyList<ReadOnlyMemory<byte>> expected)
    {
        if (!ReadQuestion(packet, transaction, owner, type, out var offset, out var answers, out var count)) return false;
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in expected) wanted.Add(Convert.ToBase64String(value.Span));
        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var name = DnsWire.ReadName(packet, ref offset); var recordType = DnsWire.Read16(packet, ref offset); var recordClass = DnsWire.Read16(packet, ref offset);
            DnsWire.RequireAvailable(packet, offset, 4); offset += 4;
            var length = DnsWire.Read16(packet, ref offset); DnsWire.RequireAvailable(packet, offset, length);
            if (i < answers)
            {
                if (recordType != type || recordClass != 1 || !string.Equals(name, owner, StringComparison.OrdinalIgnoreCase)) return false;
                var value = Convert.ToBase64String(packet.Slice(offset, length));
                if (type is 1 or 28 && !wanted.Contains(value)) return false;
                observed.Add(value);
            }
            offset += length;
        }
        if (offset != packet.Length) throw new InvalidDataException("Authoritative value response contains unframed bytes.");
        return wanted.Count == 0 ? observed.Count == 0 : wanted.IsSubsetOf(observed);
    }

    public static bool LacksValue(ReadOnlySpan<byte> packet, ushort transaction, string owner, ushort type, ReadOnlyMemory<byte> value, string origin, uint requiredSerial)
    {
        if (!ReadQuestion(packet, transaction, owner, type, out var offset, out var answers, out var count, allowNameError: true)) return false;
        var negativeSoa = false;
        var authorityEnd = answers + BinaryPrimitives.ReadUInt16BigEndian(packet[8..]);
        for (var i = 0; i < count; i++)
        {
            var name = DnsWire.ReadName(packet, ref offset); var recordType = DnsWire.Read16(packet, ref offset); var recordClass = DnsWire.Read16(packet, ref offset);
            DnsWire.RequireAvailable(packet, offset, 4); offset += 4;
            var length = DnsWire.Read16(packet, ref offset); DnsWire.RequireAvailable(packet, offset, length);
            if (i >= answers && i < authorityEnd && recordType == 6 && recordClass == 1 && string.Equals(name, origin, StringComparison.OrdinalIgnoreCase))
            {
                var position = offset;
                _ = DnsWire.ReadName(packet, ref position); _ = DnsWire.ReadName(packet, ref position);
                DnsWire.RequireAvailable(packet, position, 20);
                if (position + 20 != offset + length || !IsAtLeast(BinaryPrimitives.ReadUInt32BigEndian(packet[position..]), requiredSerial)) return false;
                negativeSoa = true;
            }
            if (i < answers && (recordType != type || recordClass != 1 || !string.Equals(name, owner, StringComparison.OrdinalIgnoreCase) || packet.Slice(offset, length).SequenceEqual(value.Span))) return false;
            offset += length;
        }
        if (offset != packet.Length) throw new InvalidDataException("Authoritative absence proof contains unframed bytes.");
        return answers != 0 || negativeSoa;
    }

    public static bool IsAtLeast(uint actual, uint required) => actual == required || unchecked(actual - required) < 0x80000000;

    private static bool ReadQuestion(ReadOnlySpan<byte> packet, ushort transaction, string name, ushort type, out int offset, out ushort answers, out int count, bool allowNameError = false)
    {
        DnsWire.RequireHeader(packet, transaction);
        var flags = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        answers = BinaryPrimitives.ReadUInt16BigEndian(packet[6..]);
        var authorities = BinaryPrimitives.ReadUInt16BigEndian(packet[8..]); var additional = BinaryPrimitives.ReadUInt16BigEndian(packet[10..]);
        count = answers + authorities + additional; offset = 12;
        if (answers > 128 || authorities + additional > 128) throw new InvalidDataException("Authoritative proof exceeds its record bound.");
        if (!string.Equals(DnsWire.ReadName(packet, ref offset), name, StringComparison.OrdinalIgnoreCase) ||
            DnsWire.Read16(packet, ref offset) != type || DnsWire.Read16(packet, ref offset) != 1)
            throw new InvalidDataException("Authoritative proof changes the requested question.");
        var rcode = flags & 0x000F;
        return (flags & 0x0280) == 0 && (flags & 0x0400) != 0 && (rcode == 0 || allowNameError && rcode == 3 && answers == 0);
    }
}
