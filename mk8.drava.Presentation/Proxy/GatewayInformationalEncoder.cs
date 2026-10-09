using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Proxy;

internal static class GatewayInformationalEncoder
{
    public static byte[] Encode(ResponseHead head, bool http2, int streamId)
    {
        FrameLimits.ValidateResponse(head);
        if (!head.Informational) throw new InvalidDataException("Expected an informational response.");
        foreach (var header in head.Headers)
            if (IsFramingField(header.Name)) throw new InvalidDataException("Informational response contains a framing or connection field.");
        return http2 ? EncodeHttp2(head, streamId) : EncodeHttp1(head);
    }

    private static bool IsFramingField(string name) =>
        name.Equals("content-length", StringComparison.OrdinalIgnoreCase)
        || name.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase)
        || name.Equals("connection", StringComparison.OrdinalIgnoreCase)
        || name.Equals("proxy-connection", StringComparison.OrdinalIgnoreCase)
        || name.Equals("keep-alive", StringComparison.OrdinalIgnoreCase)
        || name.Equals("upgrade", StringComparison.OrdinalIgnoreCase)
        || name.Equals("trailer", StringComparison.OrdinalIgnoreCase);

    private static byte[] EncodeHttp1(ResponseHead head)
    {
        var text = new StringBuilder("HTTP/1.1 ");
        text.Append(head.StatusCode.ToString(CultureInfo.InvariantCulture)).Append(" Informational\r\n");
        foreach (var header in head.Headers) text.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        text.Append("\r\n");
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static byte[] EncodeHttp2(ResponseHead head, int streamId)
    {
        if (streamId <= 0 || (streamId & 1) == 0) throw new InvalidDataException("Invalid HTTP/2 client stream identity.");
        using var block = new MemoryStream();
        // Never-indexed literals leave Kestrel's connection HPACK table untouched.
        WriteInteger(block, 0x10, 4, 8);
        WriteString(block, head.StatusCode.ToString(CultureInfo.InvariantCulture));
        foreach (var header in head.Headers)
        {
            WriteInteger(block, 0x10, 4, 0);
            var name = Encoding.ASCII.GetBytes(header.Name);
            if (Ascii.ToLowerInPlace(name, out var written) != OperationStatus.Done || written != name.Length)
                throw new InvalidDataException("HTTP/2 field name is not ASCII.");
            WriteBytes(block, name);
            WriteString(block, header.Value);
        }
        return EncodeFrames(block.ToArray(), streamId);
    }

    private static byte[] EncodeFrames(byte[] payload, int streamId)
    {
        using var frames = new MemoryStream();
        Span<byte> header = stackalloc byte[9];
        for (var offset = 0; offset < payload.Length; offset += 16384)
        {
            var length = Math.Min(16384, payload.Length - offset);
            header[0] = (byte)(length >> 16);
            header[1] = (byte)(length >> 8);
            header[2] = (byte)length;
            header[3] = offset == 0 ? (byte)1 : (byte)9;
            header[4] = offset + length == payload.Length ? (byte)4 : (byte)0;
            BinaryPrimitives.WriteInt32BigEndian(header[5..], streamId);
            frames.Write(header);
            frames.Write(payload.AsSpan(offset, length));
        }
        return frames.ToArray();
    }

    // Mechanically reused from the pinned MDRAVA HPACK integer encoder.
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

    private static void WriteString(Stream stream, string value) => WriteBytes(stream, Encoding.UTF8.GetBytes(value));

    private static void WriteBytes(Stream stream, ReadOnlySpan<byte> bytes)
    {
        WriteInteger(stream, 0, 7, bytes.Length);
        stream.Write(bytes);
    }
}
