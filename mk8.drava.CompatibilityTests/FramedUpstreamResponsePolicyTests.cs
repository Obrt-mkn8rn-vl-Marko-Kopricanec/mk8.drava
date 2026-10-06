using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class FramedUpstreamResponsePolicyTests
{
    public static void BuildsHttp1HeadFromNarrowUpstreamFacts()
    {
        var endedWithHead = Build("GET", new FramedUpstreamResponseTranslationInput(200, [new ProxyHeaderField("content-length", "12")], ResponseEndedWithHead: true));
        AssertEx.Equal(Http1BodyKind.None, endedWithHead.Framing.Kind);
        var headMethod = Build("HEAD", new FramedUpstreamResponseTranslationInput(200, [new ProxyHeaderField("content-length", "12")], ResponseEndedWithHead: false));
        AssertEx.Equal(Http1BodyKind.None, headMethod.Framing.Kind);
        var noContentStatus = Build("GET", new FramedUpstreamResponseTranslationInput(204, [new ProxyHeaderField("content-length", "12")], ResponseEndedWithHead: false));
        AssertEx.Equal(Http1BodyKind.None, noContentStatus.Framing.Kind);
        var contentLength = Build("GET", new FramedUpstreamResponseTranslationInput(200, [new ProxyHeaderField("content-length", "12")], ResponseEndedWithHead: false));
        AssertEx.Equal(Http1BodyKind.ContentLength, contentLength.Framing.Kind);
        AssertEx.Equal(12L, contentLength.Framing.ContentLength);
        AssertEx.Equal("OK", contentLength.ReasonPhrase);
        var zeroContentLength = Build("GET", new FramedUpstreamResponseTranslationInput(200, [new ProxyHeaderField("content-length", "0")], ResponseEndedWithHead: false));
        AssertEx.Equal(Http1BodyKind.None, zeroContentLength.Framing.Kind);
        var missingContentLength = Build("GET", new FramedUpstreamResponseTranslationInput(200, [], ResponseEndedWithHead: false));
        AssertEx.Equal(Http1BodyKind.Chunked, missingContentLength.Framing.Kind);
        var invalidContentLength = Reject("GET", new FramedUpstreamResponseTranslationInput(200, [new ProxyHeaderField("content-length", "-1")], ResponseEndedWithHead: false));
        AssertEx.Equal(Http1ParseErrorText.FromError(Http1ParseError.InvalidContentLength), invalidContentLength);
        var conflictingContentLength = Reject("GET", new FramedUpstreamResponseTranslationInput(200, [new ProxyHeaderField("content-length", "4"), new ProxyHeaderField("content-length", "5")], ResponseEndedWithHead: false));
        AssertEx.Equal(Http1ParseErrorText.FromError(Http1ParseError.ConflictingContentLength), conflictingContentLength);
    }

    public static void TranslationInputCopiesHeaders()
    {
        var headers = new List<ProxyHeaderField>
        {
            new("content-length", "12")
        };
        var input = new FramedUpstreamResponseTranslationInput(200, headers, ResponseEndedWithHead: false);
        headers.Clear();
        AssertEx.Equal(200, input.StatusCode);
        AssertEx.False(input.ResponseEndedWithHead);
        AssertEx.Equal("content-length", input.Headers[0].Name);
        AssertEx.False(input.Headers is ProxyHeaderField[], "Framed upstream response input headers should not expose a mutable array.");
        AssertEx.Throws<ArgumentNullException>(() => new FramedUpstreamResponseTranslationInput(200, null!, ResponseEndedWithHead: false));
    }

    private static Http1ResponseHead Build(string method, FramedUpstreamResponseTranslationInput input)
    {
        var result = FramedUpstreamResponsePolicy.BuildHttp1ResponseHead(CreateRequestHead(method), input);
        if (result is FramedUpstreamResponseTranslationResult.AcceptedResult accepted)
        {
            return accepted.ResponseHead;
        }

        throw new InvalidOperationException($"Expected accepted upstream response translation, got {result.GetType().Name}.");
    }

    private static string Reject(string method, FramedUpstreamResponseTranslationInput input)
    {
        var result = FramedUpstreamResponsePolicy.BuildHttp1ResponseHead(CreateRequestHead(method), input);
        if (result is FramedUpstreamResponseTranslationResult.RejectedResult rejected)
        {
            return rejected.Reason;
        }

        throw new InvalidOperationException($"Expected rejected upstream response translation, got {result.GetType().Name}.");
    }

    private static Http1RequestHead CreateRequestHead(string method)
    {
        return new Http1RequestHead(method, "/resource", "/resource", "HTTP/1.1", "example.test", Http1RequestFraming.None, []);
    }
}
