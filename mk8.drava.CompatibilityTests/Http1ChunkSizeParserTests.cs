using System.Text;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class Http1ChunkSizeParserTests
{
    public static void ParsesChunkSizeLine()
    {
        var parsed = Http1ChunkSizeParser.TryParseLine(Bytes("1f\r\n"), out var chunkSize);
        AssertEx.True(parsed);
        AssertEx.Equal(31L, chunkSize);
    }

    public static void ParsesChunkSizeWithExtension()
    {
        var parsed = Http1ChunkSizeParser.TryParseLine(Bytes("A;foo=bar\r\n"), out var chunkSize);
        AssertEx.True(parsed);
        AssertEx.Equal(10L, chunkSize);
    }

    public static void RejectsInvalidChunkSizeLine()
    {
        var parsed = Http1ChunkSizeParser.TryParseLine(Bytes("Z\r\n"), out _);
        AssertEx.False(parsed);
    }

    public static void RejectsMissingCrlf()
    {
        var parsed = Http1ChunkSizeParser.TryParseLine(Bytes("5"), out _);
        AssertEx.False(parsed);
    }

    public static void RejectsOverflow()
    {
        var parsed = Http1ChunkSizeParser.TryParseLine(Bytes("8000000000000000\r\n"), out _);
        AssertEx.False(parsed);
    }

    private static byte[] Bytes(string value)
    {
        return Encoding.ASCII.GetBytes(value);
    }
}
