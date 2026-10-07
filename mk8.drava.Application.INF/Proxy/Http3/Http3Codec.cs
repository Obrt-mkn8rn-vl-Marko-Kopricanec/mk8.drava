using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using System.Globalization;
using System.Text;

namespace Mk8.Drava.Application.INF.Proxy.Http3;
public static class Http3Codec
{
    public const long DataFrame = 0x0;
    public const long HeadersFrame = 0x1;
    public const long SettingsFrame = 0x4;
    public const long GoAwayFrame = 0x7;
    public const long ControlStream = 0x0;
    public const long QpackMaxTableCapacitySetting = 0x1;
    public const long QpackBlockedStreamsSetting = 0x7;
    private static readonly HeaderField[] StaticTable = [new(":authority", ""), new(":path", "/"), new("age", "0"), new("content-disposition", ""), new("content-length", "0"), new("cookie", ""), new("date", ""), new("etag", ""), new("if-modified-since", ""), new("if-none-match", ""), new("last-modified", ""), new("link", ""), new("location", ""), new("referer", ""), new("set-cookie", ""), new(":method", "CONNECT"), new(":method", "DELETE"), new(":method", "GET"), new(":method", "HEAD"), new(":method", "OPTIONS"), new(":method", "POST"), new(":method", "PUT"), new(":scheme", "http"), new(":scheme", "https"), new(":status", "103"), new(":status", "200"), new(":status", "304"), new(":status", "404"), new(":status", "503"), new("accept", "*/*"), new("accept", "application/dns-message"), new("accept-encoding", "gzip, deflate, br"), new("accept-ranges", "bytes"), new("access-control-allow-headers", "cache-control"), new("access-control-allow-headers", "content-type"), new("access-control-allow-origin", "*"), new("cache-control", "max-age=0"), new("cache-control", "max-age=2592000"), new("cache-control", "max-age=604800"), new("cache-control", "no-cache"), new("cache-control", "no-store"), new("cache-control", "public, max-age=31536000"), new("content-encoding", "br"), new("content-encoding", "gzip"), new("content-type", "application/dns-message"), new("content-type", "application/javascript"), new("content-type", "application/json"), new("content-type", "application/x-www-form-urlencoded"), new("content-type", "image/gif"), new("content-type", "image/jpeg"), new("content-type", "image/png"), new("content-type", "text/css"), new("content-type", "text/html; charset=utf-8"), new("content-type", "text/plain"), new("content-type", "text/plain;charset=utf-8"), new("range", "bytes=0-"), new("strict-transport-security", "max-age=31536000"), new("strict-transport-security", "max-age=31536000; includesubdomains"), new("strict-transport-security", "max-age=31536000; includesubdomains; preload"), new("vary", "accept-encoding"), new("vary", "origin"), new("x-content-type-options", "nosniff"), new("x-xss-protection", "1; mode=block"), new(":status", "100"), new(":status", "204"), new(":status", "206"), new(":status", "302"), new(":status", "400"), new(":status", "403"), new(":status", "421"), new(":status", "425"), new(":status", "500"), new("accept-language", ""), new("access-control-allow-credentials", "FALSE"), new("access-control-allow-credentials", "TRUE"), new("access-control-allow-headers", "*"), new("access-control-allow-methods", "get"), new("access-control-allow-methods", "get, post, options"), new("access-control-allow-methods", "options"), new("access-control-expose-headers", "content-length"), new("access-control-request-headers", "content-type"), new("access-control-request-method", "get"), new("access-control-request-method", "post"), new("alt-svc", "clear"), new("authorization", ""), new("content-security-policy", "script-src 'none'; object-src 'none'; base-uri 'none'"), new("early-data", "1"), new("expect-ct", ""), new("forwarded", ""), new("if-range", ""), new("origin", ""), new("purpose", "prefetch"), new("server", ""), new("timing-allow-origin", "*"), new("upgrade-insecure-requests", "1"), new("user-agent", ""), new("x-forwarded-for", ""), new("x-frame-options", "deny"), new("x-frame-options", "sameorigin")];
    public static byte[] EncodeHeaderBlock(IReadOnlyList<ProxyHeaderField> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        using var memory = new MemoryStream();
        memory.WriteByte(0);
        memory.WriteByte(0);
        foreach (var header in headers)
        {
            WriteLiteralHeader(memory, header.Name.ToLowerInvariant(), header.Value);
        }

        return memory.ToArray();
    }

    public static bool TryDecodeHeaderBlock(ReadOnlySpan<byte> block, int maxHeaderBytes, out IReadOnlyList<ProxyHeaderField> headers, out string reason)
    {
        headers = [];
        reason = "invalid_qpack";
        if (block.Length > maxHeaderBytes)
        {
            reason = "header_list_too_large";
            return false;
        }

        var offset = 0;
        if (!TryReadPrefixedInteger(block, 8, ref offset, out var requiredInsertCount))
        {
            return false;
        }

        var deltaBaseOffset = offset;
        if (!TryReadPrefixedInteger(block, 7, ref offset, out var deltaBase))
        {
            return false;
        }

        if (requiredInsertCount != 0 || deltaBase != 0 || (block[deltaBaseOffset] & 0x80) != 0)
        {
            reason = "unsupported_qpack_dynamic_table";
            return false;
        }

        List<ProxyHeaderField> decoded = [];
        var decodedHeaderBytes = 0;
        while (offset < block.Length)
        {
            var first = block[offset];
            if ((first & 0x80) != 0)
            {
                var isStatic = (first & 0x40) != 0;
                if (!isStatic || !TryReadPrefixedInteger(block, 6, ref offset, out var index) || !TryGetStaticField(index, out var field))
                {
                    reason = "unsupported_qpack_index";
                    return false;
                }

                if (!TryAddDecodedHeader(decoded, new ProxyHeaderField(field.Name, field.Value), maxHeaderBytes, ref decodedHeaderBytes, out reason))
                {
                    return false;
                }

                continue;
            }

            if ((first & 0x40) != 0)
            {
                var isStatic = (first & 0x10) != 0;
                if (!isStatic || !TryReadPrefixedInteger(block, 4, ref offset, out var nameIndex) || !TryGetStaticField(nameIndex, out var namedField) || !TryReadString(block, ref offset, out var value, out reason))
                {
                    reason = "unsupported_qpack_name_ref";
                    return false;
                }

                if (!TryAddDecodedHeader(decoded, new ProxyHeaderField(namedField.Name, value), maxHeaderBytes, ref decodedHeaderBytes, out reason))
                {
                    return false;
                }

                continue;
            }

            if ((first & 0x20) != 0)
            {
                var literalRead = ReadLiteralHeader(block, ref offset);
                if (literalRead is not QpackLiteralHeaderReadResult.Decoded decodedLiteral)
                {
                    reason = ((QpackLiteralHeaderReadResult.Rejected)literalRead).Reason;
                    return false;
                }

                if (!TryAddDecodedHeader(decoded, decodedLiteral.Header, maxHeaderBytes, ref decodedHeaderBytes, out reason))
                {
                    return false;
                }

                continue;
            }

            reason = "unsupported_qpack_field";
            return false;
        }

        headers = decoded;
        return true;
    }

    private static bool TryAddDecodedHeader(List<ProxyHeaderField> headers, ProxyHeaderField header, int maxHeaderBytes, ref int decodedHeaderBytes, out string reason)
    {
        decodedHeaderBytes += header.Name.Length + header.Value.Length;
        if (decodedHeaderBytes > maxHeaderBytes)
        {
            reason = "header_list_too_large";
            return false;
        }

        headers.Add(header);
        reason = "";
        return true;
    }

    public static void WriteFrame(Stream stream, long type, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(stream);
        WriteVarInt(stream, type);
        WriteVarInt(stream, payload.Length);
        stream.Write(payload);
    }

    public static void WriteVarInt(Stream stream, long value)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (value < 0 || value >= 1L << 62)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (value < 64)
        {
            stream.WriteByte((byte)value);
            return;
        }

        if (value < 16_384)
        {
            stream.WriteByte((byte)(0x40 | (value >> 8)));
            stream.WriteByte((byte)value);
            return;
        }

        if (value < 1_073_741_824)
        {
            stream.WriteByte((byte)(0x80 | (value >> 24)));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
            return;
        }

        stream.WriteByte((byte)(0xC0 | (value >> 56)));
        stream.WriteByte((byte)(value >> 48));
        stream.WriteByte((byte)(value >> 40));
        stream.WriteByte((byte)(value >> 32));
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    public static bool TryReadVarInt(ReadOnlySpan<byte> bytes, ref int offset, out long value)
    {
        value = 0;
        if (offset >= bytes.Length)
        {
            return false;
        }

        var first = bytes[offset++];
        var length = 1 << (first >> 6);
        value = first & 0x3f;
        if (offset + length - 1 > bytes.Length)
        {
            return false;
        }

        for (var index = 1; index < length; index++)
        {
            value = (value << 8) | bytes[offset++];
        }

        return true;
    }

    public static bool TryReadFrame(ReadOnlySpan<byte> bytes, ref int offset, out long type, out ReadOnlyMemory<byte> payload)
    {
        type = 0;
        payload = ReadOnlyMemory<byte>.Empty;
        if (!TryReadVarInt(bytes, ref offset, out type) || !TryReadVarInt(bytes, ref offset, out var length) || length < 0 || length > int.MaxValue || offset + (int)length > bytes.Length)
        {
            return false;
        }

        payload = bytes.Slice(offset, (int)length).ToArray();
        offset += (int)length;
        return true;
    }

    private static QpackLiteralHeaderReadResult ReadLiteralHeader(ReadOnlySpan<byte> block, ref int offset)
    {
        var nameHuffman = offset < block.Length && (block[offset] & 0x08) != 0;
        if (!TryReadPrefixedInteger(block, 3, ref offset, out var nameLength) || nameLength < 0 || nameLength > int.MaxValue || offset + (int)nameLength > block.Length)
        {
            return QpackLiteralHeaderReadResult.Reject("invalid_qpack_literal");
        }

        if (!TryDecodeStringBytes(block.Slice(offset, (int)nameLength), nameHuffman, out var name, out var reason))
        {
            return QpackLiteralHeaderReadResult.Reject(reason);
        }

        offset += (int)nameLength;
        if (!TryReadString(block, ref offset, out var value, out reason))
        {
            return QpackLiteralHeaderReadResult.Reject(reason);
        }

        return QpackLiteralHeaderReadResult.Decode(new ProxyHeaderField(name, value));
    }

    private abstract record QpackLiteralHeaderReadResult
    {
        private QpackLiteralHeaderReadResult()
        {
        }

        public static QpackLiteralHeaderReadResult Decode(ProxyHeaderField header)
        {
            return new Decoded(header);
        }

        public static QpackLiteralHeaderReadResult Reject(string reason)
        {
            return new Rejected(reason);
        }

        public sealed record Decoded : QpackLiteralHeaderReadResult
        {
            public Decoded(ProxyHeaderField header)
            {
                ArgumentNullException.ThrowIfNull(header);
                Header = header;
            }

            public ProxyHeaderField Header { get; }
        }

        public sealed record Rejected : QpackLiteralHeaderReadResult
        {
            public Rejected(string reason)
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new ArgumentException("QPACK literal header rejection reason is required.", nameof(reason));
                }

                Reason = reason;
            }

            public string Reason { get; }
        }
    }

    private static void WriteLiteralHeader(Stream stream, string name, string value)
    {
        WritePrefixedInteger(stream, 0x20, 3, Encoding.ASCII.GetByteCount(name));
        stream.Write(Encoding.ASCII.GetBytes(name));
        WriteString(stream, value);
    }

    private static bool TryReadString(ReadOnlySpan<byte> block, ref int offset, out string value, out string reason)
    {
        value = "";
        reason = "invalid_qpack_string";
        if (offset >= block.Length)
        {
            return false;
        }

        var huffman = (block[offset] & 0x80) != 0;
        if (!TryReadPrefixedInteger(block, 7, ref offset, out var length) || length < 0 || length > int.MaxValue || offset + (int)length > block.Length)
        {
            return false;
        }

        if (!TryDecodeStringBytes(block.Slice(offset, (int)length), huffman, out value, out reason))
        {
            return false;
        }

        offset += (int)length;
        return true;
    }

    private static bool TryDecodeStringBytes(ReadOnlySpan<byte> bytes, bool huffman, out string value, out string reason)
    {
        reason = "";
        if (huffman)
        {
            if (!Http3HpackHuffmanDecoder.TryDecode(bytes, out var decoded))
            {
                value = "";
                reason = "invalid_huffman";
                return false;
            }

            value = Encoding.ASCII.GetString(decoded);
            return true;
        }

        value = Encoding.ASCII.GetString(bytes);
        return true;
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        WritePrefixedInteger(stream, 0, 7, bytes.Length);
        stream.Write(bytes);
    }

    private static bool TryReadPrefixedInteger(ReadOnlySpan<byte> block, int prefixBits, ref int offset, out long value)
    {
        value = 0;
        if (offset >= block.Length)
        {
            return false;
        }

        var mask = (1 << prefixBits) - 1;
        value = block[offset++] & mask;
        if (value < mask)
        {
            return true;
        }

        var multiplier = 0;
        while (offset < block.Length)
        {
            var next = block[offset++];
            value += (long)(next & 0x7f) << multiplier;
            if ((next & 0x80) == 0)
            {
                return true;
            }

            multiplier += 7;
            if (multiplier > 56)
            {
                return false;
            }
        }

        return false;
    }

    private static void WritePrefixedInteger(Stream stream, byte prefix, int prefixBits, int value)
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

    private static bool TryGetStaticField(long index, out HeaderField field)
    {
        field = default;
        if (index < 0 || index >= StaticTable.Length)
        {
            return false;
        }

        field = StaticTable[(int)index];
        return true;
    }

    public static string StatusText(int statusCode)
    {
        return statusCode.ToString(CultureInfo.InvariantCulture);
    }

    private readonly record struct HeaderField(string Name, string Value);
}
