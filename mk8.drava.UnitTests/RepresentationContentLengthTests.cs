using System.Text;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.INF.Proxy.Exchange;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RepresentationContentLengthTests
{
    [Theory]
    [InlineData("HEAD", 200, "42", 42L)]
    [InlineData("HEAD", 200, "0", 0L)]
    [InlineData("HEAD", 200, null, null)]
    [InlineData("GET", 304, "42", 42L)]
    [InlineData("GET", 304, null, null)]
    [InlineData("GET", 204, null, null)]
    public async Task LivePresentationRetainsRepresentationMetadataWithZeroBodyBytesAsync(string method, int status, string? value, long? expected)
    {
        var head = Parse(method, status, value);
        Assert.Equal(Http1BodyKind.None, head.Framing.Kind);
        var length = ProxyResponseContentLengthPolicy.GetContentLength(head, 0);
        Assert.Equal(expected, length);
        using var wire = new MemoryStream();
        await Http1ResponseHeadWriter.WriteAsync(wire, head, head.Headers, [], "req", length, false, false,
            TimeSpan.FromSeconds(5), new ProxyMetrics(), CancellationToken.None).ConfigureAwait(true);
        var frames = new ExchangeFrameWriter();
        using var typed = new ExchangeResponseWriter(frames, method, static _ => ValueTask.CompletedTask);
        await typed.WriteAsync(wire.ToArray(), CancellationToken.None).ConfigureAwait(true);
        await typed.CompleteAsync(CancellationToken.None).ConfigureAwait(true);
        var fields = Assert.IsType<Mk8.Drava.Transport.Protocol.V1.ResponseHead>(frames.Frames[0].Response).Headers.Where(static field => string.Equals(field.Name, "content-length", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (expected is { } number) Assert.Collection(fields, field => Assert.Equal(number.ToString(System.Globalization.CultureInfo.InvariantCulture), field.Value));
        else Assert.Empty(fields);
        Assert.Equal(0ul, Assert.IsType<Mk8.Drava.Transport.Protocol.V1.Completion>(frames.Frames[^1].Complete).BodyBytes);
        Assert.DoesNotContain(frames.Frames, static frame => frame.Data is not null);
    }

    [Theory]
    [InlineData("HEAD", 200, "+42")]
    [InlineData("HEAD", 200, "42, 43")]
    [InlineData("GET", 304, "+42")]
    [InlineData("GET", 204, "0")]
    [InlineData("GET", 100, "0")]
    public void BodylessLengthSyntaxAndProhibitedStatusFieldsAreRejected(string method, int status, string value)
    {
        var bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Length: {value}\r\n\r\n");
        Assert.False(Http1ResponseParser.TryParse(bytes, method, out _, out var error));
        Assert.True(error is Http1ParseError.InvalidContentLength or Http1ParseError.ConflictingContentLength);
    }

    [Theory]
    [InlineData("HEAD", 200, "42", 42L)]
    [InlineData("HEAD", 200, null, null)]
    [InlineData("GET", 304, "42", 42L)]
    [InlineData("GET", 304, null, null)]
    public void CacheHitsRetainKnownAndAbsentRepresentationLengths(string method, int status, string? value, long? expected)
    {
        var cache = new ResponseCacheStore(TimeProvider.System);
        var policy = new ProxyCachePolicyFacts(true, 4096, 8192, TimeSpan.FromSeconds(60), false, [], [200, 304], ["GET", "HEAD"]);
        var scope = new ProxyCacheRequestScope("route", "peer.test", "https", policy);
        var request = new Http1RequestHead(method, "/", "/", "HTTP/1.1", "peer.test", Http1RequestFraming.None, []);
        var response = Parse(method, status, value);
        cache.Store(scope, request, "/", response, response.Headers, []);
        var stored = Assert.IsType<ProxyCacheLookupResult.HitResult>(cache.Get(scope, request, "/")).Response;
        Assert.Equal(expected, stored.ContentLength);
        Assert.Empty(stored.Body);
        var fields = ProxyCachedResponseHeaderPolicy.BuildFramedResponseHeaders(stored, "req", DateTimeOffset.UtcNow)
            .Where(static field => string.Equals(field.Name, "content-length", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (expected is { } number) Assert.Collection(fields, field => Assert.Equal(number.ToString(System.Globalization.CultureInfo.InvariantCulture), field.Value));
        else Assert.Empty(fields);
    }

    [Fact]
    public void OrdinaryEmptyResponsesStillHaveAnExplicitZeroLength()
    {
        var request = new Http1RequestHead("GET", "/", "/", "HTTP/1.1", "peer.test", Http1RequestFraming.None, []);
        var head = Assert.IsType<FramedUpstreamResponseTranslationResult.AcceptedResult>(FramedUpstreamResponsePolicy.BuildHttp1ResponseHead(request,
            new FramedUpstreamResponseTranslationInput(200, [], true))).ResponseHead;
        Assert.Equal(0L, ProxyResponseContentLengthPolicy.GetContentLength(head));
        Assert.Equal(0L, ProxyResponseContentLengthPolicy.GetContentLength(head, 0));
    }

    private static Http1ResponseHead Parse(string method, int status, string? value)
    {
        var length = value is null ? "" : "Content-Length: " + value + "\r\n";
        var bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\n{length}\r\n");
        Assert.True(Http1ResponseParser.TryParse(bytes, method, out var response, out var error), error.ToString());
        return Assert.IsType<Http1ResponseHead>(response);
    }
}
