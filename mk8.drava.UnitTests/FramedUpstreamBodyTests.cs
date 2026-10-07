using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class FramedUpstreamBodyTests
{
    [Theory]
    [InlineData("GET", 200, "3", true, false)]
    [InlineData("GET", 200, "0", true, true)]
    [InlineData("HEAD", 200, "3", true, true)]
    [InlineData("GET", 304, "3", true, true)]
    [InlineData("HEAD", 200, "+3", true, false)]
    [InlineData("GET", 304, "+3", true, false)]
    [InlineData("GET", 204, "0", true, false)]
    [InlineData("GET", 100, "0", false, false)]
    [InlineData("GET", 200, "3, 4", true, false)]
    public void HeadTranslationValidatesLengthBeforeEndAndBodylessDecisions(string method, int status, string length, bool ended, bool accepted)
    {
        var request = new Http1RequestHead(method, "/", "/", "HTTP/1.1", "peer.test", Http1RequestFraming.None, []);
        var result = FramedUpstreamResponsePolicy.BuildHttp1ResponseHead(request,
            new FramedUpstreamResponseTranslationInput(status, [new("content-length", length)], ended));
        Assert.Equal(accepted, result is FramedUpstreamResponseTranslationResult.AcceptedResult);
        if (accepted) Assert.Equal(Http1BodyKind.None, Assert.IsType<FramedUpstreamResponseTranslationResult.AcceptedResult>(result).ResponseHead.Framing.Kind);
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 4, false)]
    [InlineData(3, 2, true)]
    public void FixedLengthRejectsExcessOrIncompleteContent(long expected, int bytes, bool ended)
    {
        var body = new FramedUpstreamBodyLength(Http1ResponseFraming.FromContentLength(expected));
        Assert.Throws<FramedUpstreamProtocolException>(() => body.Observe(bytes, ended));
    }

    [Fact]
    public void FragmentsAndEmptyFramesFinishOnlyAtTheActualEnd()
    {
        var body = new FramedUpstreamBodyLength(Http1ResponseFraming.FromContentLength(3));
        body.Observe(1, false); body.Observe(0, false); body.Observe(2, false); body.Observe(0, true);
        Assert.Throws<FramedUpstreamProtocolException>(() => body.Observe(0, true));
    }

    [Fact]
    public void ABodylessResponseMustStillWaitForItsEmptyEndingFrame()
    {
        var body = new FramedUpstreamBodyLength(Http1ResponseFraming.None);
        body.Observe(0, false);
        Assert.Throws<FramedUpstreamProtocolException>(() => body.Observe(1, true));
    }

    [Fact]
    public void UnknownLengthCanStreamUntilEndWithoutAFalseFixedLengthLimit()
    {
        var body = new FramedUpstreamBodyLength(Http1ResponseFraming.Chunked);
        body.Observe(int.MaxValue, false); body.Observe(int.MaxValue, true);
        Assert.Throws<FramedUpstreamProtocolException>(() => body.Observe(1, false));
    }

    [Theory]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public void ExistingCachePolicyRejectsAnAdvertisedOversizedBodyBeforeBuffering(long bytes, bool accepted)
    {
        var policy = new ProxyCachePolicyFacts(true, 4096, 8192, TimeSpan.FromSeconds(60), true, [], [200], ["GET"]);
        var response = new Http1ResponseHead("HTTP/1.1", 200, "OK", Http1ResponseFraming.FromContentLength(bytes), []);
        var request = new Http1RequestHead("GET", "/", "/", "HTTP/1.1", "peer.test", Http1RequestFraming.None, []);
        Assert.Equal(accepted, ProxyCacheEligibilityPolicy.EvaluateResponseForBuffering(policy, request, response)
            is ProxyCacheEligibilityResult.AcceptedResult);
    }
}
