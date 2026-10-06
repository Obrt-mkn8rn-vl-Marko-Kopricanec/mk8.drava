using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class Http1RequestFramingPolicyTests
{
    public static void ClassifiesFramedRequestBodies()
    {
        AssertEx.False(Http1RequestFramingPolicy.HasFramedBody(CreateHead(Http1RequestFraming.None)));
        AssertEx.False(Http1RequestFramingPolicy.HasFramedBody(CreateHead(Http1RequestFraming.FromContentLength(0))));
        AssertEx.True(Http1RequestFramingPolicy.HasFramedBody(CreateHead(Http1RequestFraming.FromContentLength(1))));
        AssertEx.True(Http1RequestFramingPolicy.HasFramedBody(CreateHead(Http1RequestFraming.Chunked)));
    }

    private static Http1RequestHead CreateHead(Http1RequestFraming framing)
    {
        return new Http1RequestHead("POST", "/upload", "/upload", "HTTP/1.1", "example.test", framing, []);
    }
}
