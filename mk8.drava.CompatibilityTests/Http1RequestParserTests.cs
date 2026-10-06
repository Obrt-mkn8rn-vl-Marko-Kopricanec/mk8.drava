using System.Text;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class Http1RequestParserTests
{
    public static void ParsesValidGet()
    {
        var bytes = Bytes("GET /health?verbose=true HTTP/1.1\r\nHost: example.test:8080\r\nUser-Agent: test\r\n\r\n");
        var parsed = Http1RequestParser.TryParse(bytes, out var requestHead, out var error);
        AssertEx.True(parsed);
        AssertEx.Equal(Http1ParseError.None, error);
        var head = AssertEx.NotNull(requestHead);
        AssertEx.Equal("GET", head.Method);
        AssertEx.Equal("/health?verbose=true", head.Target);
        AssertEx.Equal("/health", head.Path);
        AssertEx.Equal("HTTP/1.1", head.Version);
        AssertEx.Equal("example.test:8080", head.Host);
        AssertEx.Equal(null, head.ContentLength);
        AssertEx.False(head.HasTransferEncoding);
    }

    public static void HttpHeadsCopyInputHeaderLists()
    {
        var requestHeaders = new List<ProxyHeaderField>
        {
            new("Host", "example.test")
        };
        var responseHeaders = new List<ProxyHeaderField>
        {
            new("Content-Type", "text/plain")
        };
        var diagnosticsHeaders = new List<ProxyHeaderField>
        {
            new("X-Debug", "true")
        };
        var requestHead = new Http1RequestHead("GET", "/", "/", "HTTP/1.1", "example.test", Http1RequestFraming.None, requestHeaders.Select(static header => header));
        var responseHead = new Http1ResponseHead("HTTP/1.1", 200, "OK", Http1ResponseFraming.None, responseHeaders.Select(static header => header));
        var diagnosticsHead = new ProxyRouteDiagnosticsRequestHead("GET", "/", "/", "HTTP/1.1", "example.test", ProxyRouteDiagnosticsRequestFraming.None, diagnosticsHeaders.Select(static header => header));
        requestHeaders.Clear();
        responseHeaders.Clear();
        diagnosticsHeaders.Clear();
        AssertEx.Equal("Host", requestHead.Headers[0].Name);
        AssertEx.False(requestHead.Headers is ProxyHeaderField[], "HTTP/1 request headers should not expose a mutable array.");
        AssertEx.Equal("Content-Type", responseHead.Headers[0].Name);
        AssertEx.False(responseHead.Headers is ProxyHeaderField[], "HTTP/1 response headers should not expose a mutable array.");
        AssertEx.Equal("X-Debug", diagnosticsHead.Headers[0].Name);
        AssertEx.False(diagnosticsHead.Headers is ProxyHeaderField[], "Route diagnostics headers should not expose a mutable array.");
        AssertEx.Throws<ArgumentNullException>(() => new Http1RequestHead("GET", "/", "/", "HTTP/1.1", "example.test", Http1RequestFraming.None, [null!]));
        AssertEx.Throws<ArgumentNullException>(() => new Http1ResponseHead("HTTP/1.1", 200, "OK", Http1ResponseFraming.None, [null!]));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyRouteDiagnosticsRequestHead("GET", "/", "/", "HTTP/1.1", "example.test", ProxyRouteDiagnosticsRequestFraming.None, [null!]));
        AssertEx.Throws<ArgumentNullException>(() => new FramedUpstreamResponseTranslationInput(200, [null!], ResponseEndedWithHead: false));
        AssertEx.Throws<ArgumentNullException>(() => new GeneratedRouteResponse(200, "OK", null, "", [null!]));
        AssertEx.Throws<ArgumentException>(() => new Http1RequestFraming(Http1BodyKind.None, 1));
        AssertEx.Throws<ArgumentException>(() => new Http1RequestFraming(Http1BodyKind.Chunked, 1));
        AssertEx.Throws<ArgumentException>(() => new Http1RequestFraming(Http1BodyKind.ContentLength, null));
        AssertEx.Throws<ArgumentException>(() => new Http1RequestFraming(Http1BodyKind.ContentLength, 0));
        AssertEx.Throws<ArgumentException>(() => new Http1RequestFraming(Http1BodyKind.CloseDelimited, null));
        AssertEx.Throws<ArgumentException>(() => new Http1RequestFraming((Http1BodyKind)99, null));
        AssertEx.Throws<ArgumentException>(() => new Http1ResponseFraming(Http1BodyKind.None, 1));
        AssertEx.Throws<ArgumentException>(() => new Http1ResponseFraming(Http1BodyKind.Chunked, 1));
        AssertEx.Throws<ArgumentException>(() => new Http1ResponseFraming(Http1BodyKind.CloseDelimited, 1));
        AssertEx.Throws<ArgumentException>(() => new Http1ResponseFraming(Http1BodyKind.ContentLength, null));
        AssertEx.Throws<ArgumentException>(() => new Http1ResponseFraming(Http1BodyKind.ContentLength, 0));
        AssertEx.Throws<ArgumentException>(() => new Http1ResponseFraming((Http1BodyKind)99, null));
    }

    public static void RejectsMissingHost()
    {
        var bytes = Bytes("GET / HTTP/1.1\r\nUser-Agent: test\r\n\r\n");
        var parsed = Http1RequestParser.TryParse(bytes, out var requestHead, out var error);
        AssertEx.False(parsed);
        AssertEx.Equal(Http1ParseError.MissingHost, error);
        AssertEx.Equal(null, requestHead);
    }

    public static void RejectsInvalidContentLength()
    {
        var bytes = Bytes("GET / HTTP/1.1\r\nHost: example.test\r\nContent-Length: nope\r\n\r\n");
        var parsed = Http1RequestParser.TryParse(bytes, out _, out var error);
        AssertEx.False(parsed);
        AssertEx.Equal(Http1ParseError.InvalidContentLength, error);
    }

    public static void DetectsRequestBodyIndicators()
    {
        var bytes = Bytes("GET / HTTP/1.1\r\nHost: example.test\r\nContent-Length: 12\r\n\r\n");
        var parsed = Http1RequestParser.TryParse(bytes, out var requestHead, out var error);
        AssertEx.True(parsed);
        AssertEx.Equal(Http1ParseError.None, error);
        var head = AssertEx.NotNull(requestHead);
        AssertEx.Equal(12L, head.ContentLength);
        AssertEx.False(head.HasTransferEncoding);
        AssertEx.Equal(Http1BodyKind.ContentLength, head.Framing.Kind);
    }

    public static void ParsesChunkedTransferEncoding()
    {
        var bytes = Bytes("POST / HTTP/1.1\r\nHost: example.test\r\nTransfer-Encoding: chunked\r\n\r\n");
        var parsed = Http1RequestParser.TryParse(bytes, out var requestHead, out var error);
        AssertEx.True(parsed);
        AssertEx.Equal(Http1ParseError.None, error);
        AssertEx.Equal(Http1BodyKind.Chunked, AssertEx.NotNull(requestHead).Framing.Kind);
    }

    public static void RejectsConflictingContentLength()
    {
        var bytes = Bytes("POST / HTTP/1.1\r\nHost: example.test\r\nContent-Length: 1\r\nContent-Length: 2\r\n\r\n");
        var parsed = Http1RequestParser.TryParse(bytes, out _, out var error);
        AssertEx.False(parsed);
        AssertEx.Equal(Http1ParseError.ConflictingContentLength, error);
    }

    public static void RejectsContentLengthWithTransferEncoding()
    {
        var bytes = Bytes("POST / HTTP/1.1\r\nHost: example.test\r\nContent-Length: 1\r\nTransfer-Encoding: chunked\r\n\r\n");
        var parsed = Http1RequestParser.TryParse(bytes, out _, out var error);
        AssertEx.False(parsed);
        AssertEx.Equal(Http1ParseError.ContentLengthWithTransferEncoding, error);
    }

    public static void RejectsUnsupportedTransferEncoding()
    {
        var bytes = Bytes("POST / HTTP/1.1\r\nHost: example.test\r\nTransfer-Encoding: gzip\r\n\r\n");
        var parsed = Http1RequestParser.TryParse(bytes, out _, out var error);
        AssertEx.False(parsed);
        AssertEx.Equal(Http1ParseError.UnsupportedTransferEncoding, error);
    }

    public static void ContentLengthAnalysisNamesAcceptedLength()
    {
        var result = Http1RequestParser.AnalyzeContentLength(["12", "12"]);
        AssertEx.True(result is Http1ContentLengthAnalysisResult.Accepted);
        AssertEx.Equal(12L, ((Http1ContentLengthAnalysisResult.Accepted)result).ContentLength);
    }

    public static void ContentLengthAnalysisNamesRejectedLength()
    {
        var invalid = Http1RequestParser.AnalyzeContentLength(["nope"]);
        var conflicting = Http1RequestParser.AnalyzeContentLength(["1", "2"]);
        AssertEx.True(invalid is Http1ContentLengthAnalysisResult.Rejected);
        AssertEx.Equal(Http1ParseError.InvalidContentLength, ((Http1ContentLengthAnalysisResult.Rejected)invalid).Error);
        AssertEx.True(conflicting is Http1ContentLengthAnalysisResult.Rejected);
        AssertEx.Equal(Http1ParseError.ConflictingContentLength, ((Http1ContentLengthAnalysisResult.Rejected)conflicting).Error);
    }

    public static void TransferEncodingAnalysisNamesAcceptedChunked()
    {
        var result = Http1RequestParser.AnalyzeTransferEncoding(["chunked"]);
        AssertEx.Equal(Http1TransferEncodingAnalysisResult.Accepted, result);
    }

    public static void TransferEncodingAnalysisNamesRejectedCodings()
    {
        var invalid = Http1RequestParser.AnalyzeTransferEncoding([""]);
        var unsupported = Http1RequestParser.AnalyzeTransferEncoding(["gzip", "chunked"]);
        AssertEx.True(invalid is Http1TransferEncodingAnalysisResult.Rejected);
        AssertEx.Equal(Http1ParseError.InvalidTransferEncoding, ((Http1TransferEncodingAnalysisResult.Rejected)invalid).Error);
        AssertEx.True(unsupported is Http1TransferEncodingAnalysisResult.Rejected);
        AssertEx.Equal(Http1ParseError.UnsupportedTransferEncoding, ((Http1TransferEncodingAnalysisResult.Rejected)unsupported).Error);
    }

    private static byte[] Bytes(string value)
    {
        return Encoding.ASCII.GetBytes(value);
    }
}
