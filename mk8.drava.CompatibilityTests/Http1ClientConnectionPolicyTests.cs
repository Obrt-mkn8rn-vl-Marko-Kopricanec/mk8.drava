using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class Http1ClientConnectionPolicyTests
{
    public static void KeepsHttp11OpenByDefault()
    {
        var requestHead = Request("HTTP/1.1");
        AssertEx.True(Http1ClientConnectionPolicy.ShouldKeepOpen(requestHead));
    }

    public static void ClosesWhenConnectionCloseIsPresent()
    {
        var requestHead = Request("HTTP/1.1", new ProxyHeaderField("Connection", "keep-alive, close"));
        AssertEx.False(Http1ClientConnectionPolicy.ShouldKeepOpen(requestHead));
    }

    public static void ClosesHttp10ByDefault()
    {
        var requestHead = Request("HTTP/1.0", new ProxyHeaderField("Connection", "keep-alive"));
        AssertEx.False(Http1ClientConnectionPolicy.ShouldKeepOpen(requestHead));
    }

    private static Http1RequestHead Request(string version, params ProxyHeaderField[] headers)
    {
        return new Http1RequestHead("GET", "/", "/", version, "example.test", Http1RequestFraming.None, headers);
    }
}
