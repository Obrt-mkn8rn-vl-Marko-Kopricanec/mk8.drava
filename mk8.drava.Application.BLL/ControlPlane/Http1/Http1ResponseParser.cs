using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public static class Http1ResponseParser
{
    private static Http1ResponseFramingAnalysisResult AnalyzeResponseFraming(string requestMethod, int statusCode, List<string> contentLengthValues, List<string> transferEncodingValues)
    {
        if (IsNoBodyResponse(requestMethod, statusCode))
        {
            if (contentLengthValues.Count > 0)
            {
                if (statusCode is >= 100 and < 200 or 204)
                    return Http1ResponseFramingAnalysisResult.Reject(Http1ParseError.InvalidContentLength);
                if (Http1RequestParser.AnalyzeContentLength(contentLengthValues) is Http1ContentLengthAnalysisResult.Rejected rejected)
                    return Http1ResponseFramingAnalysisResult.Reject(rejected.Error);
            }
            return Http1ResponseFramingAnalysisResult.Accept(Http1ResponseFraming.None);
        }

        if (transferEncodingValues.Count > 0)
        {
            var transferEncodingAnalysis = Http1RequestParser.AnalyzeTransferEncoding(transferEncodingValues);
            if (transferEncodingAnalysis is Http1TransferEncodingAnalysisResult.Rejected rejectedTransferEncoding)
            {
                return Http1ResponseFramingAnalysisResult.Reject(rejectedTransferEncoding.Error);
            }

            return Http1ResponseFramingAnalysisResult.Accept(Http1ResponseFraming.Chunked);
        }

        if (contentLengthValues.Count > 0)
        {
            var contentLengthAnalysis = Http1RequestParser.AnalyzeContentLength(contentLengthValues);
            if (contentLengthAnalysis is Http1ContentLengthAnalysisResult.Rejected rejectedContentLength)
            {
                return Http1ResponseFramingAnalysisResult.Reject(rejectedContentLength.Error);
            }

            var contentLength = ((Http1ContentLengthAnalysisResult.Accepted)contentLengthAnalysis).ContentLength;
            return Http1ResponseFramingAnalysisResult.Accept(Http1ResponseFraming.FromContentLength(contentLength));
        }

        return Http1ResponseFramingAnalysisResult.Accept(Http1ResponseFraming.CloseDelimited);
    }

    private abstract record Http1ResponseFramingAnalysisResult
    {
        private Http1ResponseFramingAnalysisResult()
        {
        }

        public static Accepted Accept(Http1ResponseFraming framing)
        {
            ArgumentNullException.ThrowIfNull(framing);
            return new Accepted(framing);
        }

        public static Rejected Reject(Http1ParseError error)
        {
            if (error == Http1ParseError.None)
            {
                throw new ArgumentException("Response framing rejection requires a parse error.", nameof(error));
            }

            return new Rejected(error);
        }

        public sealed record Accepted(Http1ResponseFraming Framing) : Http1ResponseFramingAnalysisResult;
        public sealed record Rejected(Http1ParseError Error) : Http1ResponseFramingAnalysisResult;
    }

    public static bool TryParse(ReadOnlySpan<byte> responseHeadBytes, string requestMethod, [NotNullWhen(true)] out Http1ResponseHead? responseHead, out Http1ParseError error)
    {
        responseHead = null;
        error = Http1ParseError.None;
        if (!TryReadStatusLine(responseHeadBytes, out var statusLineLength, out var statusCode, out var reasonPhrase, out error))
            return false;

        List<ProxyHeaderField> headers = [];
        List<string> contentLengthValues = [];
        List<string> transferEncodingValues = [];
        var nextLineStart = statusLineLength + 2;
        while (nextLineStart < responseHeadBytes.Length)
        {
            var remaining = responseHeadBytes[nextLineStart..];
            var lineLength = IndexOfCrlf(remaining);
            if (lineLength < 0)
            {
                error = Http1ParseError.InvalidHeaderLine;
                return false;
            }

            if (lineLength == 0)
            {
                break;
            }

            var headerLine = remaining[..lineLength];
            var colon = headerLine.IndexOf((byte)':');
            if (colon <= 0)
            {
                error = Http1ParseError.InvalidHeaderLine;
                return false;
            }

            var name = Encoding.ASCII.GetString(Trim(headerLine[..colon]));
            var value = Encoding.ASCII.GetString(Trim(headerLine[(colon + 1)..]));
            headers.Add(new ProxyHeaderField(name, value));
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                contentLengthValues.Add(value);
            }
            else if (string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                transferEncodingValues.Add(value);
            }

            nextLineStart += lineLength + 2;
        }

        var framingAnalysis = AnalyzeResponseFraming(requestMethod, statusCode, contentLengthValues, transferEncodingValues);
        if (framingAnalysis is Http1ResponseFramingAnalysisResult.Rejected rejectedFraming)
        {
            error = rejectedFraming.Error;
            return false;
        }

        var framing = ((Http1ResponseFramingAnalysisResult.Accepted)framingAnalysis).Framing;
        responseHead = new Http1ResponseHead("HTTP/1.1", statusCode, reasonPhrase, framing, headers, string.Equals(requestMethod, "HEAD", StringComparison.Ordinal));
        return true;
    }

    private static bool TryReadStatusLine(ReadOnlySpan<byte> responseHeadBytes, out int statusLineLength,
        out int statusCode, out string reasonPhrase, out Http1ParseError error)
    {
        statusCode = 0;
        reasonPhrase = "";
        error = Http1ParseError.None;
        statusLineLength = IndexOfCrlf(responseHeadBytes);
        if (statusLineLength <= 0)
        {
            error = Http1ParseError.InvalidStatusLine;
            return false;
        }

        var statusLine = responseHeadBytes[..statusLineLength];
        var firstSpace = statusLine.IndexOf((byte)' ');
        if (firstSpace <= 0)
        {
            error = Http1ParseError.InvalidStatusLine;
            return false;
        }

        var versionBytes = statusLine[..firstSpace];
        if (!AsciiEquals(versionBytes, "HTTP/1.1"))
        {
            error = Http1ParseError.UnsupportedVersion;
            return false;
        }

        var statusAndReason = statusLine[(firstSpace + 1)..];
        if (statusAndReason.Length < 3 || !TryParseStatusCode(statusAndReason[..3], out statusCode))
        {
            error = Http1ParseError.InvalidStatusLine;
            return false;
        }

        reasonPhrase = statusAndReason.Length > 3 && statusAndReason[3] == (byte)' ' ? Encoding.ASCII.GetString(statusAndReason[4..]) : "";
        return true;
    }

    public static bool IsInformational(Http1ResponseHead responseHead)
    {
        ArgumentNullException.ThrowIfNull(responseHead);
        return responseHead.StatusCode is >= 100 and <= 199;
    }

    public static bool IsNoBodyResponse(string requestMethod, int statusCode)
    {
        return string.Equals(requestMethod, "HEAD", StringComparison.Ordinal) || (statusCode is >= 100 and <= 199 or 204 or 304);
    }

    private static bool TryParseStatusCode(ReadOnlySpan<byte> bytes, out int statusCode)
    {
        statusCode = 0;
        if (bytes.Length != 3)
        {
            return false;
        }

        foreach (ref readonly var digit in bytes)
        {
            if (digit is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            statusCode = (statusCode * 10) + digit - (byte)'0';
        }

        return true;
    }

    private static int IndexOfCrlf(ReadOnlySpan<byte> bytes)
    {
        for (var index = 1; index < bytes.Length; index++)
        {
            if (bytes[index - 1] == (byte)'\r' && bytes[index] == (byte)'\n')
            {
                return index - 1;
            }
        }

        return -1;
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> bytes)
    {
        while (bytes.Length > 0 && IsOptionalWhitespace(bytes[0]))
        {
            bytes = bytes[1..];
        }

        while (bytes.Length > 0 && IsOptionalWhitespace(bytes[^1]))
        {
            bytes = bytes[..^1];
        }

        return bytes;
    }

    private static bool IsOptionalWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t';
    }

    private static bool AsciiEquals(ReadOnlySpan<byte> bytes, string text)
    {
        if (bytes.Length != text.Length)
        {
            return false;
        }

        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] != text[index])
            {
                return false;
            }
        }

        return true;
    }
}
