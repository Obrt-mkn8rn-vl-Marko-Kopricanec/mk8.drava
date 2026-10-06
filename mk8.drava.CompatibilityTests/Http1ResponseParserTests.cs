using System.Text;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class Http1ResponseParserTests
{
    public static void ParsesContentLengthResponse()
    {
        var parsed = Http1ResponseParser.TryParse(Bytes("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\n"), "GET", out var response, out var error);
        AssertEx.True(parsed);
        AssertEx.Equal(Http1ParseError.None, error);
        AssertEx.Equal(Http1BodyKind.ContentLength, AssertEx.NotNull(response).Framing.Kind);
        AssertEx.Equal(5L, AssertEx.NotNull(response).Framing.ContentLength);
    }

    public static void TreatsZeroContentLengthAsNoBody()
    {
        var parsed = Http1ResponseParser.TryParse(Bytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"), "GET", out var response, out var error);
        AssertEx.True(parsed);
        AssertEx.Equal(Http1ParseError.None, error);
        AssertEx.Equal(Http1BodyKind.None, AssertEx.NotNull(response).Framing.Kind);
    }

    public static void ParsesChunkedResponse()
    {
        var parsed = Http1ResponseParser.TryParse(Bytes("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n"), "GET", out var response, out var error);
        AssertEx.True(parsed);
        AssertEx.Equal(Http1ParseError.None, error);
        AssertEx.Equal(Http1BodyKind.Chunked, AssertEx.NotNull(response).Framing.Kind);
    }

    public static void TreatsHeadResponseAsNoBody()
    {
        var parsed = Http1ResponseParser.TryParse(Bytes("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\n"), "HEAD", out var response, out var error);
        AssertEx.True(parsed);
        AssertEx.Equal(Http1ParseError.None, error);
        AssertEx.Equal(Http1BodyKind.None, AssertEx.NotNull(response).Framing.Kind);
    }

    public static void TreatsNoContentAsNoBody()
    {
        var parsed = Http1ResponseParser.TryParse(Bytes("HTTP/1.1 204 No Content\r\nContent-Length: 5\r\n\r\n"), "GET", out var response, out var error);
        AssertEx.True(parsed);
        AssertEx.Equal(Http1ParseError.None, error);
        AssertEx.Equal(Http1BodyKind.None, AssertEx.NotNull(response).Framing.Kind);
    }

    public static void TreatsNotModifiedAsNoBody()
    {
        var parsed = Http1ResponseParser.TryParse(Bytes("HTTP/1.1 304 Not Modified\r\nContent-Length: 5\r\n\r\n"), "GET", out var response, out var error);
        AssertEx.True(parsed);
        AssertEx.Equal(Http1ParseError.None, error);
        AssertEx.Equal(Http1BodyKind.None, AssertEx.NotNull(response).Framing.Kind);
    }

    public static void RejectsInvalidResponseContentLength()
    {
        var parsed = Http1ResponseParser.TryParse(Bytes("HTTP/1.1 200 OK\r\nContent-Length: no\r\n\r\n"), "GET", out _, out var error);
        AssertEx.False(parsed);
        AssertEx.Equal(Http1ParseError.InvalidContentLength, error);
    }

    private static byte[] Bytes(string value)
    {
        return Encoding.ASCII.GetBytes(value);
    }
}
