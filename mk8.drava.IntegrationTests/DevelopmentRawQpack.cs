using System.Text;
using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.IntegrationTests;

// Native QPACK literals preserve the supplied fields so negative tests can emit malformed names.
internal static class DevelopmentRawQpack
{
    public static byte[] EncodeHeaderBlock(IReadOnlyList<ProxyHeaderField> headers)
    {
        using var memory = new MemoryStream();
        memory.WriteByte(0);
        memory.WriteByte(0);
        foreach (var header in headers) WriteLiteralHeader(memory, header.Name, header.Value);
        return memory.ToArray();
    }

    private static void WriteLiteralHeader(Stream stream, string name, string value)
    {
        WritePrefixedInteger(stream, 0x20, 3, Encoding.ASCII.GetByteCount(name));
        stream.Write(Encoding.ASCII.GetBytes(name));
        WriteString(stream, value);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        WritePrefixedInteger(stream, 0, 7, bytes.Length);
        stream.Write(bytes);
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
}
