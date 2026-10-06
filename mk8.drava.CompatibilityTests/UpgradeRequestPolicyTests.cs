using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class UpgradeRequestPolicyTests
{
    public static void ValidatesAcceptedWebSocketUpgrade()
    {
        var policy = new UpgradeRequestPolicy();
        var requestHead = RequestHead("GET", Http1RequestFraming.None, new ProxyHeaderField("Connection", "keep-alive, Upgrade"), new ProxyHeaderField("Upgrade", "websocket"), new ProxyHeaderField("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ=="), new ProxyHeaderField("Sec-WebSocket-Version", "13"));
        var decision = policy.Validate(requestHead);
        AssertEx.True(decision is UpgradeRequestValidationDecision.AcceptedDecision);
        var accepted = (UpgradeRequestValidationDecision.AcceptedDecision)decision;
        AssertEx.Equal("websocket", accepted.Upgrade.Protocol);
        AssertEx.True(accepted.Upgrade.IsWebSocket);
        AssertEx.Equal("dGhlIHNhbXBsZSBub25jZQ==", accepted.Upgrade.WebSocketKey);
    }

    public static void RejectsUpgradeRequestBodies()
    {
        var policy = new UpgradeRequestPolicy();
        var requestHead = RequestHead("GET", Http1RequestFraming.FromContentLength(5), new ProxyHeaderField("Connection", "Upgrade"), new ProxyHeaderField("Upgrade", "websocket"), new ProxyHeaderField("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ=="), new ProxyHeaderField("Sec-WebSocket-Version", "13"));
        var decision = policy.Validate(requestHead);
        AssertEx.True(decision is UpgradeRequestValidationDecision.RejectedDecision);
        var rejection = (UpgradeRequestValidationDecision.RejectedDecision)decision;
        AssertEx.Equal("HTTP Upgrade request bodies are not supported in Phase 7.", rejection.Reason);
    }

    public static void ClassifiesManagedUpgradeHeaders()
    {
        AssertEx.True(UpgradeRequestPolicy.IsManagedUpgradeHeader("Connection"));
        AssertEx.True(UpgradeRequestPolicy.IsManagedUpgradeHeader("upgrade"));
        AssertEx.False(UpgradeRequestPolicy.IsManagedUpgradeHeader("Sec-WebSocket-Accept"));
    }

    public static void ClassifiesUnsafeSwitchingProtocolsResponseHeaders()
    {
        AssertEx.True(UpgradeRequestPolicy.IsUnsafeSwitchingProtocolsResponseHeader("Keep-Alive"));
        AssertEx.True(UpgradeRequestPolicy.IsUnsafeSwitchingProtocolsResponseHeader("proxy-authenticate"));
        AssertEx.True(UpgradeRequestPolicy.IsUnsafeSwitchingProtocolsResponseHeader("Proxy-Authorization"));
        AssertEx.True(UpgradeRequestPolicy.IsUnsafeSwitchingProtocolsResponseHeader("TE"));
        AssertEx.True(UpgradeRequestPolicy.IsUnsafeSwitchingProtocolsResponseHeader("Trailer"));
        AssertEx.True(UpgradeRequestPolicy.IsUnsafeSwitchingProtocolsResponseHeader("transfer-encoding"));
        AssertEx.False(UpgradeRequestPolicy.IsUnsafeSwitchingProtocolsResponseHeader("Sec-WebSocket-Accept"));
    }

    private static Http1RequestHead RequestHead(string method, Http1RequestFraming framing, params ProxyHeaderField[] headers)
    {
        return new Http1RequestHead(method, "/chat", "/chat", "HTTP/1.1", "example.test", framing, headers);
    }
}
