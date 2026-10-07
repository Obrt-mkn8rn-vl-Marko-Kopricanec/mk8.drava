using System.Globalization;
using System.Text;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;

namespace Mk8.Drava.Application.INF.Proxy.Http2;
internal static class HpackCodec
{
    private static readonly HeaderField[] StaticTable = [new("", ""), new(":authority", ""), new(":method", "GET"), new(":method", "POST"), new(":path", "/"), new(":path", "/index.html"), new(":scheme", "http"), new(":scheme", "https"), new(":status", "200"), new(":status", "204"), new(":status", "206"), new(":status", "304"), new(":status", "400"), new(":status", "404"), new(":status", "500"), new("accept-charset", ""), new("accept-encoding", "gzip, deflate"), new("accept-language", ""), new("accept-ranges", ""), new("accept", ""), new("access-control-allow-origin", ""), new("age", ""), new("allow", ""), new("authorization", ""), new("cache-control", ""), new("content-disposition", ""), new("content-encoding", ""), new("content-language", ""), new("content-length", ""), new("content-location", ""), new("content-range", ""), new("content-type", ""), new("cookie", ""), new("date", ""), new("etag", ""), new("expect", ""), new("expires", ""), new("from", ""), new("host", ""), new("if-match", ""), new("if-modified-since", ""), new("if-none-match", ""), new("if-range", ""), new("if-unmodified-since", ""), new("last-modified", ""), new("link", ""), new("location", ""), new("max-forwards", ""), new("proxy-authenticate", ""), new("proxy-authorization", ""), new("range", ""), new("referer", ""), new("refresh", ""), new("retry-after", ""), new("server", ""), new("set-cookie", ""), new("strict-transport-security", ""), new("transfer-encoding", ""), new("user-agent", ""), new("vary", ""), new("via", ""), new("www-authenticate", "")];
    private static readonly uint[] HuffmanCodes = [0x1ff8u, 0x7fffd8u, 0xfffffe2u, 0xfffffe3u, 0xfffffe4u, 0xfffffe5u, 0xfffffe6u, 0xfffffe7u, 0xfffffe8u, 0xffffeau, 0x3ffffffcu, 0xfffffe9u, 0xfffffeau, 0x3ffffffdu, 0xfffffebu, 0xfffffecu, 0xfffffedu, 0xfffffeeu, 0xfffffefu, 0xffffff0u, 0xffffff1u, 0xffffff2u, 0x3ffffffeu, 0xffffff3u, 0xffffff4u, 0xffffff5u, 0xffffff6u, 0xffffff7u, 0xffffff8u, 0xffffff9u, 0xffffffau, 0xffffffbu, 0x14u, 0x3f8u, 0x3f9u, 0xffau, 0x1ff9u, 0x15u, 0xf8u, 0x7fau, 0x3fau, 0x3fbu, 0xf9u, 0x7fbu, 0xfau, 0x16u, 0x17u, 0x18u, 0x0u, 0x1u, 0x2u, 0x19u, 0x1au, 0x1bu, 0x1cu, 0x1du, 0x1eu, 0x1fu, 0x5cu, 0xfbu, 0x7ffcu, 0x20u, 0xffbu, 0x3fcu, 0x1ffau, 0x21u, 0x5du, 0x5eu, 0x5fu, 0x60u, 0x61u, 0x62u, 0x63u, 0x64u, 0x65u, 0x66u, 0x67u, 0x68u, 0x69u, 0x6au, 0x6bu, 0x6cu, 0x6du, 0x6eu, 0x6fu, 0x70u, 0x71u, 0x72u, 0xfcu, 0x73u, 0xfdu, 0x1ffbu, 0x7fff0u, 0x1ffcu, 0x3ffcu, 0x22u, 0x7ffdu, 0x3u, 0x23u, 0x4u, 0x24u, 0x5u, 0x25u, 0x26u, 0x27u, 0x6u, 0x74u, 0x75u, 0x28u, 0x29u, 0x2au, 0x7u, 0x2bu, 0x76u, 0x2cu, 0x8u, 0x9u, 0x2du, 0x77u, 0x78u, 0x79u, 0x7au, 0x7bu, 0x7ffeu, 0x7fcu, 0x3ffdu, 0x1ffdu, 0xffffffcu, 0xfffe6u, 0x3fffd2u, 0xfffe7u, 0xfffe8u, 0x3fffd3u, 0x3fffd4u, 0x3fffd5u, 0x7fffd9u, 0x3fffd6u, 0x7fffdau, 0x7fffdbu, 0x7fffdcu, 0x7fffddu, 0x7fffdeu, 0xffffebu, 0x7fffdfu, 0xffffecu, 0xffffedu, 0x3fffd7u, 0x7fffe0u, 0xffffeeu, 0x7fffe1u, 0x7fffe2u, 0x7fffe3u, 0x7fffe4u, 0x1fffdcu, 0x3fffd8u, 0x7fffe5u, 0x3fffd9u, 0x7fffe6u, 0x7fffe7u, 0xffffefu, 0x3fffdau, 0x1fffddu, 0xfffe9u, 0x3fffdbu, 0x3fffdcu, 0x7fffe8u, 0x7fffe9u, 0x1fffdeu, 0x7fffeau, 0x3fffddu, 0x3fffdeu, 0xfffff0u, 0x1fffdfu, 0x3fffdfu, 0x7fffebu, 0x7fffecu, 0x1fffe0u, 0x1fffe1u, 0x3fffe0u, 0x1fffe2u, 0x7fffedu, 0x3fffe1u, 0x7fffeeu, 0x7fffefu, 0xfffeau, 0x3fffe2u, 0x3fffe3u, 0x3fffe4u, 0x7ffff0u, 0x3fffe5u, 0x3fffe6u, 0x7ffff1u, 0x3ffffe0u, 0x3ffffe1u, 0xfffebu, 0x7fff1u, 0x3fffe7u, 0x7ffff2u, 0x3fffe8u, 0x1ffffecu, 0x3ffffe2u, 0x3ffffe3u, 0x3ffffe4u, 0x7ffffdeu, 0x7ffffdfu, 0x3ffffe5u, 0xfffff1u, 0x1ffffedu, 0x7fff2u, 0x1fffe3u, 0x3ffffe6u, 0x7ffffe0u, 0x7ffffe1u, 0x3ffffe7u, 0x7ffffe2u, 0xfffff2u, 0x1fffe4u, 0x1fffe5u, 0x3ffffe8u, 0x3ffffe9u, 0xffffffdu, 0x7ffffe3u, 0x7ffffe4u, 0x7ffffe5u, 0xfffecu, 0xfffff3u, 0xfffedu, 0x1fffe6u, 0x3fffe9u, 0x1fffe7u, 0x1fffe8u, 0x7ffff3u, 0x3fffeau, 0x3fffebu, 0x1ffffeeu, 0x1ffffefu, 0xfffff4u, 0xfffff5u, 0x3ffffeau, 0x7ffff4u, 0x3ffffebu, 0x7ffffe6u, 0x3ffffecu, 0x3ffffedu, 0x7ffffe7u, 0x7ffffe8u, 0x7ffffe9u, 0x7ffffeau, 0x7ffffebu, 0xffffffeu, 0x7ffffecu, 0x7ffffedu, 0x7ffffeeu, 0x7ffffefu, 0x7fffff0u, 0x3ffffeeu, 0x3fffffffu];
    private static readonly byte[] HuffmanCodeLengths = [13, 23, 28, 28, 28, 28, 28, 28, 28, 24, 30, 28, 28, 30, 28, 28, 28, 28, 28, 28, 28, 28, 30, 28, 28, 28, 28, 28, 28, 28, 28, 28, 6, 10, 10, 12, 13, 6, 8, 11, 10, 10, 8, 11, 8, 6, 6, 6, 5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 7, 8, 15, 6, 12, 10, 13, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 7, 8, 13, 19, 13, 14, 6, 15, 5, 6, 5, 6, 5, 6, 6, 6, 5, 7, 7, 6, 6, 6, 5, 6, 7, 6, 5, 5, 6, 7, 7, 7, 7, 7, 15, 11, 14, 13, 28, 20, 22, 20, 20, 22, 22, 22, 23, 22, 23, 23, 23, 23, 23, 24, 23, 24, 24, 22, 23, 24, 23, 23, 23, 23, 21, 22, 23, 22, 23, 23, 24, 22, 21, 20, 22, 22, 23, 23, 21, 23, 22, 22, 24, 21, 22, 23, 23, 21, 21, 22, 21, 23, 22, 23, 23, 20, 22, 22, 22, 23, 22, 22, 23, 26, 26, 20, 19, 22, 23, 22, 25, 26, 26, 26, 27, 27, 26, 24, 25, 19, 21, 26, 27, 27, 26, 27, 24, 21, 21, 26, 26, 28, 27, 27, 27, 20, 24, 20, 21, 22, 21, 21, 23, 22, 22, 25, 25, 24, 24, 26, 23, 26, 27, 26, 26, 27, 27, 27, 27, 27, 28, 27, 27, 27, 27, 27, 26, 30];
    private static readonly IReadOnlyDictionary<ulong, int> HuffmanDecodeTable = BuildHuffmanDecodeTable();
    public static bool TryDecodeRequestHeaders(byte[] block, out IReadOnlyList<ProxyHeaderField> headers, out string reason)
    {
        headers = [];
        reason = "invalid_hpack";
        List<ProxyHeaderField> decoded = [];
        List<HeaderField> dynamicTable = [];
        var offset = 0;
        while (offset < block.Length)
        {
            var current = block[offset];
            if ((current & 0x80) != 0)
            {
                var index = DecodeInteger(block, 7, ref offset);
                if (!TryGetHeader(index, dynamicTable, out var field))
                {
                    reason = "invalid_hpack_index";
                    return false;
                }

                decoded.Add(new ProxyHeaderField(field.Name, field.Value));
                continue;
            }

            if ((current & 0x40) != 0)
            {
                if (!TryDecodeLiteral(block, 6, ref offset, dynamicTable, out var literal, out reason))
                {
                    return false;
                }

                dynamicTable.Insert(0, literal);
                decoded.Add(new ProxyHeaderField(literal.Name, literal.Value));
                continue;
            }

            if ((current & 0x20) != 0)
            {
                SkipInteger(block, 5, ref offset);
                continue;
            }

            var prefix = (current & 0x10) != 0 ? 4 : 4;
            if (!TryDecodeLiteral(block, prefix, ref offset, dynamicTable, out var withoutIndex, out reason))
            {
                return false;
            }

            decoded.Add(new ProxyHeaderField(withoutIndex.Name, withoutIndex.Value));
        }

        headers = decoded;
        return true;
    }

    public static byte[] EncodeRequestHeaders(IReadOnlyList<ProxyHeaderField> headers)
    {
        using var memory = new MemoryStream();
        foreach (var header in headers)
        {
            var name = header.Name.ToLowerInvariant();
            if (Http2HeaderPolicy.IsForbiddenRequestHeader(name, header.Value))
            {
                continue;
            }

            WriteInteger(memory, 0x00, 4, StaticNameIndex(name));
            if (StaticNameIndex(name) == 0)
            {
                WriteString(memory, name);
            }

            WriteString(memory, header.Value);
        }

        return memory.ToArray();
    }

    public static byte[] EncodeResponseHeaders(int statusCode, IReadOnlyList<ProxyHeaderField> headers)
    {
        using var memory = new MemoryStream();
        var indexedStatus = statusCode switch
        {
            200 => 8,
            204 => 9,
            206 => 10,
            304 => 11,
            400 => 12,
            404 => 13,
            500 => 14,
            _ => 0
        };
        if (indexedStatus > 0)
        {
            WriteInteger(memory, 0x80, 7, indexedStatus);
        }
        else
        {
            WriteInteger(memory, 0x00, 4, 8);
            WriteString(memory, statusCode.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var header in headers)
        {
            if (Http2HeaderPolicy.IsForbiddenResponseHeader(header.Name))
            {
                continue;
            }

            WriteInteger(memory, 0x00, 4, StaticNameIndex(header.Name));
            if (StaticNameIndex(header.Name) == 0)
            {
                WriteString(memory, header.Name.ToLowerInvariant());
            }

            WriteString(memory, header.Value);
        }

        return memory.ToArray();
    }

    private static bool TryDecodeLiteral(byte[] block, int prefixBits, ref int offset, IReadOnlyList<HeaderField> dynamicTable, out HeaderField field, out string reason)
    {
        field = new HeaderField("", "");
        reason = "invalid_hpack_literal";
        var nameIndex = DecodeInteger(block, prefixBits, ref offset);
        string name;
        if (nameIndex == 0)
        {
            if (!TryReadString(block, ref offset, out name, out reason))
            {
                return false;
            }
        }
        else
        {
            if (!TryGetHeader(nameIndex, dynamicTable, out var indexed))
            {
                reason = "invalid_hpack_index";
                return false;
            }

            name = indexed.Name;
        }

        if (!TryReadString(block, ref offset, out var value, out reason))
        {
            return false;
        }

        field = new HeaderField(name, value);
        return true;
    }

    private static bool TryReadString(byte[] block, ref int offset, out string value, out string reason)
    {
        value = "";
        reason = "invalid_hpack_string";
        if (offset >= block.Length)
        {
            return false;
        }

        var huffman = (block[offset] & 0x80) != 0;
        var length = DecodeInteger(block, 7, ref offset);
        if (length < 0 || offset + length > block.Length)
        {
            return false;
        }

        if (huffman)
        {
            if (!TryDecodeHuffman(block.AsSpan(offset, length), out value))
            {
                reason = "invalid_huffman";
                return false;
            }
        }
        else
        {
            value = Encoding.ASCII.GetString(block, offset, length);
        }

        offset += length;
        return true;
    }

    private static bool TryDecodeHuffman(ReadOnlySpan<byte> bytes, out string value)
    {
        value = "";
        List<byte> decoded = [];
        var code = 0u;
        var length = 0;
        foreach (var current in bytes)
        {
            for (var bitIndex = 7; bitIndex >= 0; bitIndex--)
            {
                code = (code << 1) | (uint)((current >> bitIndex) & 1);
                length++;
                if (HuffmanDecodeTable.TryGetValue(HuffmanKey(length, code), out var symbol))
                {
                    if (symbol == 256)
                    {
                        return false;
                    }

                    decoded.Add((byte)symbol);
                    code = 0;
                    length = 0;
                    continue;
                }

                if (length > 30)
                {
                    return false;
                }
            }
        }

        if (length > 7)
        {
            return false;
        }

        if (length > 0 && code != ((1u << length) - 1))
        {
            return false;
        }

        value = Encoding.ASCII.GetString(decoded.ToArray());
        return true;
    }

    private static IReadOnlyDictionary<ulong, int> BuildHuffmanDecodeTable()
    {
        Dictionary<ulong, int> table = new();
        for (var symbol = 0; symbol < HuffmanCodes.Length; symbol++)
        {
            table[HuffmanKey(HuffmanCodeLengths[symbol], HuffmanCodes[symbol])] = symbol;
        }

        return table;
    }

    private static ulong HuffmanKey(int length, uint code)
    {
        return ((ulong)length << 32) | code;
    }

    private static int DecodeInteger(byte[] block, int prefixBits, ref int offset)
    {
        var mask = (1 << prefixBits) - 1;
        var value = block[offset++] & mask;
        if (value < mask)
        {
            return value;
        }

        var multiplier = 0;
        while (offset < block.Length)
        {
            var next = block[offset++];
            value += (next & 0x7f) << multiplier;
            if ((next & 0x80) == 0)
            {
                break;
            }

            multiplier += 7;
        }

        return value;
    }

    private static void SkipInteger(byte[] block, int prefixBits, ref int offset)
    {
        DecodeInteger(block, prefixBits, ref offset);
    }

    private static void WriteInteger(Stream stream, byte prefix, int prefixBits, int value)
    {
        var maxPrefix = (1 << prefixBits) - 1;
        if (value < maxPrefix)
        {
            stream.WriteByte((byte)(prefix | value));
            return;
        }

        stream.WriteByte((byte)(prefix | maxPrefix));
        value -= maxPrefix;
        while (value >= 128)
        {
            stream.WriteByte((byte)(value % 128 + 128));
            value /= 128;
        }

        stream.WriteByte((byte)value);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        WriteInteger(stream, 0, 7, bytes.Length);
        stream.Write(bytes);
    }

    private static bool TryGetHeader(int index, IReadOnlyList<HeaderField> dynamicTable, out HeaderField field)
    {
        field = default;
        if (index > 0 && index < StaticTable.Length)
        {
            field = StaticTable[index];
            return true;
        }

        var dynamicIndex = index - StaticTable.Length;
        if (dynamicIndex >= 0 && dynamicIndex < dynamicTable.Count)
        {
            field = dynamicTable[dynamicIndex];
            return true;
        }

        return false;
    }

    private static int StaticNameIndex(string name)
    {
        for (var index = 1; index < StaticTable.Length; index++)
        {
            if (string.Equals(StaticTable[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private readonly record struct HeaderField(string Name, string Value);
}
