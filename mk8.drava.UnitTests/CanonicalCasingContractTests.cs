using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class CanonicalCasingContractTests
{
    [Theory]
    [InlineData("\u2003HtTp1\u2003", RuntimeListenerProtocols.Http1)]
    [InlineData("HTTP2", RuntimeListenerProtocols.Http2)]
    [InlineData("Http1AndHttp2", RuntimeListenerProtocols.Http1AndHttp2)]
    [InlineData("http3", RuntimeListenerProtocols.Http3)]
    [InlineData("HTTP1ANDHTTP3", RuntimeListenerProtocols.Http1AndHttp3)]
    [InlineData("Http2AndHttp3", RuntimeListenerProtocols.Http2AndHttp3)]
    [InlineData("HTTP1ANDHTTP2ANDHTTP3", RuntimeListenerProtocols.Http1AndHttp2AndHttp3)]
    public void AsciiProtocolVocabularyKeepsItsConfiguredMeaning(string text, RuntimeListenerProtocols expected)
    {
        var result = Assert.IsType<RuntimeListenerProtocolParseResult.AcceptedResult>(RuntimeHttp3Compatibility.ParseProtocols(text));
        Assert.Equal(expected, result.Protocols);
    }

    [Theory]
    [InlineData("\u2003DeFaUlT\u2003", RuntimeHttp3Enablement.Default)]
    [InlineData(" DISABLED ", RuntimeHttp3Enablement.Disabled)]
    public void ExplicitAsciiEnablementKeepsItsConfiguredMeaning(string text, RuntimeHttp3Enablement expected)
    {
        var result = Assert.IsType<RuntimeHttp3EnablementParseResult.AcceptedResult>(RuntimeHttp3Compatibility.ParseEnablement(text));
        Assert.Equal(expected, result.Enablement);
        Assert.True(result.ExplicitlyConfigured);
    }

    [Theory]
    [InlineData("di\u017Fabled", "HTTP\uFF11")]
    [InlineData("d\u0131sabled", "\u210Ettp1")]
    public void NonAsciiLookalikesRemainRejected(string enablement, string protocol)
    {
        Assert.IsType<RuntimeHttp3EnablementParseResult.RejectedResult>(RuntimeHttp3Compatibility.ParseEnablement(enablement));
        Assert.Same(RuntimeListenerProtocolParseResult.Rejected, RuntimeHttp3Compatibility.ParseProtocols(protocol));
    }

    [Fact]
    public void PublicListenerIdentityTextRetainsItsCanonicalLowercaseRepresentation()
    {
        // RFC3849 documentation address; these identity-only objects never bind a socket.
        var tcp = new RuntimeListenerIdentity(Name: " Worker-A ", Address: " 2001:DB8::1 ", Port: 443, Transport: RuntimeListenerTransport.Https, TlsEnabled: true);
        var quic = new RuntimeQuicListenerIdentity(Name: " Worker-A ", Address: " 2001:DB8::1 ", Port: 443, TlsEnabled: true);
        var status = new ProxyQuicListenerIdentity(Name: " Worker-A ", Address: " 2001:DB8::1 ", Port: 443, TlsEnabled: true);
        Assert.Equal("worker-a", tcp.Key);
        Assert.Equal("2001:db8::1|443|https", tcp.BindKey);
        Assert.Equal("worker-a|quic", quic.Key);
        Assert.Equal("2001:db8::1|443|udp|quic", quic.BindKey);
        Assert.Equal(quic.Key, status.Key);
        Assert.Equal(quic.BindKey, status.BindKey);
    }

    [Theory]
    [InlineData("/AdMiN", true)]
    [InlineData("/Public", false)]
    [InlineData("/Auth/Profile", true)]
    public void PrivateCacheLintRecognizesAsciiCaseWithoutRewritingPaths(string path, bool expected)
    {
        var route = new ProxyConfigLintRoute(Name: "fixture", SiteName: "fixture", Host: "fixture.invalid", PathPrefix: path, Action: "proxy", HttpsRedirectEnabled: false, CanonicalHostEnabled: false, CanonicalHostTargetHost: "", CacheEnabled: true, CacheVaryByHeaders: [], RetryEnabled: false, RetryMethods: [], HealthCheckEnabled: false, Upstreams: [], StaticResponseBody: "");
        var findings = ConfigLintRouteCacheAnalyzer.Analyze(route, "fixture", sourceName: null);
        Assert.Equal(expected, findings.Count != 0);
        Assert.Equal(path, route.PathPrefix);
    }
}
