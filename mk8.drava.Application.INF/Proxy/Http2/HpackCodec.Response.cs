using System.Text;
using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.Application.INF.Proxy.Http2;

internal static partial class HpackCodec
{
    // Native upstream SETTINGS advertises HEADER_TABLE_SIZE=0. Incremental
    // literals remain legal, but no entry survives for a later indexed field.
    public static bool TryDecodeResponseHeaders(byte[] block, int maximumBytes, int maximumCount,
        out IReadOnlyList<ProxyHeaderField> headers, out string reason)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
        headers = [];
        reason = "invalid_hpack";
        List<ProxyHeaderField> decoded = [];
        var offset = 0;
        var bytes = 0;
        var updates = 0;
        while (offset < block.Length)
        {
            var current = block[offset];
            if ((current & 0xe0) == 0x20)
            {
                if (DecodeInteger(block, 5, ref offset) != 0 || decoded.Count != 0 || ++updates > 2)
                {
                    reason = "invalid_hpack_table_size";
                    return false;
                }
                continue;
            }
            if (!TryReadResponseField(block, ref offset, out var field, out reason)) return false;
            var fieldBytes = Encoding.UTF8.GetByteCount(field.Name) + Encoding.UTF8.GetByteCount(field.Value);
            if (decoded.Count == maximumCount || fieldBytes > maximumBytes - bytes)
            {
                reason = "hpack_field_limit";
                return false;
            }
            bytes += fieldBytes;
            decoded.Add(new ProxyHeaderField(field.Name, field.Value));
        }
        headers = decoded.ToArray();
        return true;
    }

    private static bool TryReadResponseField(byte[] block, ref int offset, out HeaderField field, out string reason)
    {
        var current = block[offset];
        reason = "invalid_hpack_index";
        if ((current & 0x80) != 0)
            return TryGetHeader(DecodeInteger(block, 7, ref offset), [], out field);
        return TryDecodeLiteral(block, (current & 0x40) != 0 ? 6 : 4, ref offset, [], out field, out reason);
    }
}
