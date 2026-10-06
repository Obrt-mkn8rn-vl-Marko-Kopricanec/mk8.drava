using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class Http2HeaderPolicyTests
{
    public static void ClassifiesAllowedRequestPseudoHeaders()
    {
        AssertEx.True(Http2HeaderPolicy.IsAllowedRequestPseudoHeader(":method"));
        AssertEx.True(Http2HeaderPolicy.IsAllowedRequestPseudoHeader(":scheme"));
        AssertEx.True(Http2HeaderPolicy.IsAllowedRequestPseudoHeader(":authority"));
        AssertEx.True(Http2HeaderPolicy.IsAllowedRequestPseudoHeader(":path"));
        AssertEx.True(Http2HeaderPolicy.IsAllowedRequestPseudoHeader(":protocol"));
        AssertEx.False(Http2HeaderPolicy.IsAllowedRequestPseudoHeader(":status"));
    }

    public static void ClassifiesForbiddenRequestHeaders()
    {
        AssertEx.True(Http2HeaderPolicy.IsForbiddenRequestHeader("connection", "close"));
        AssertEx.True(Http2HeaderPolicy.IsForbiddenRequestHeader("te", "gzip"));
        AssertEx.False(Http2HeaderPolicy.IsForbiddenRequestHeader("te", "trailers"));
        AssertEx.False(Http2HeaderPolicy.IsForbiddenRequestHeader("x-application", "value"));
    }

    public static void ClassifiesManagedUpstreamRequestHeaders()
    {
        AssertEx.True(Http2HeaderPolicy.IsManagedUpstreamRequestHeader(":method"));
        AssertEx.True(Http2HeaderPolicy.IsManagedUpstreamRequestHeader("Host"));
        AssertEx.True(Http2HeaderPolicy.IsManagedUpstreamRequestHeader("content-length"));
        AssertEx.True(Http2HeaderPolicy.IsManagedUpstreamRequestHeader("Transfer-Encoding"));
        AssertEx.True(Http2HeaderPolicy.IsManagedUpstreamRequestHeader("Upgrade"));
        AssertEx.True(Http2HeaderPolicy.IsManagedUpstreamRequestHeader("Keep-Alive"));
        AssertEx.True(Http2HeaderPolicy.IsManagedUpstreamRequestHeader("Proxy-Connection"));
        AssertEx.True(Http2HeaderPolicy.IsManagedUpstreamRequestHeader("x-request-id"));
        AssertEx.False(Http2HeaderPolicy.IsManagedUpstreamRequestHeader("x-application"));
    }
}
