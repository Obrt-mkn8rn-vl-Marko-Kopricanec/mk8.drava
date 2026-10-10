using System.Text;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class Http1DecimalLengthBoundsTests
{
    [Theory]
    [InlineData("27670116110564327423", "request")]
    [InlineData("27670116110564327423", "response")]
    [InlineData("27670116110564327423", "head")]
    [InlineData("27670116110564327423", "not-modified")]
    [InlineData("27670116110564327420", "request")]
    [InlineData("27670116110564327420", "response")]
    [InlineData("27670116110564327420", "head")]
    [InlineData("27670116110564327420", "not-modified")]
    [InlineData("46116860184273879039", "request")]
    [InlineData("46116860184273879039", "response")]
    [InlineData("46116860184273879039", "head")]
    [InlineData("46116860184273879039", "not-modified")]
    public void DecimalLengthsOutsideInt64NeverBecomeAcceptedWrappedLengths(string length, string context)
    {
        var bytes = Encoding.ASCII.GetBytes(Header(length, context));
        bool accepted;
        Http1ParseError error;
        if (string.Equals(context, "request", StringComparison.Ordinal))
            accepted = Http1RequestParser.TryParse(bytes, out _, out error);
        else
            accepted = Http1ResponseParser.TryParse(bytes, string.Equals(context, "head", StringComparison.Ordinal) ? "HEAD" : "GET", out _, out error);
        Assert.False(accepted);
        Assert.Equal(Http1ParseError.InvalidContentLength, error);
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("1", 1L)]
    [InlineData("31457280", 31457280L)]
    [InlineData("268500992", 268500992L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    public void ValidBoundaryLengthsRetainTheirExactNumericValue(string text, long value)
    {
        var result = Assert.IsType<Http1ContentLengthAnalysisResult.Accepted>(Http1RequestParser.AnalyzeContentLength([text]));
        Assert.Equal(value, result.ContentLength);
    }

    private static string Header(string length, string context) => string.Equals(context, "request", StringComparison.Ordinal)
        ? $"POST /mail/a%2Fb?sig=a%2Bb%2Fc%3D HTTP/1.1\r\nHost: service.invalid\r\nContent-Length: {length}\r\n\r\n"
        : $"HTTP/1.1 {(string.Equals(context, "not-modified", StringComparison.Ordinal) ? "304 Not Modified" : "200 OK")}\r\nContent-Length: {length}\r\n\r\n";
}
