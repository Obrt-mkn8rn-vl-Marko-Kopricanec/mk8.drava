using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;

namespace Mk8.Drava.Application.BLL.ControlPlane.Upgrades;
public sealed class UpgradeRequestPolicy
{
    public bool IsUpgradeRequest(Http1RequestHead requestHead)
    {
        ArgumentNullException.ThrowIfNull(requestHead);
        return HopByHopHeaderPolicy.HasConnectionToken(requestHead.Headers, "upgrade") || HasHeader(requestHead.Headers, "Upgrade");
    }

    public UpgradeRequestValidationDecision Validate(Http1RequestHead requestHead)
    {
        ArgumentNullException.ThrowIfNull(requestHead);
        if (!string.Equals(requestHead.Version, "HTTP/1.1", StringComparison.OrdinalIgnoreCase))
        {
            return UpgradeRequestValidationDecision.Rejected("HTTP Upgrade requires HTTP/1.1.");
        }

        if (!HopByHopHeaderPolicy.HasConnectionToken(requestHead.Headers, "upgrade"))
        {
            return UpgradeRequestValidationDecision.Rejected("HTTP Upgrade requires Connection: Upgrade.");
        }

        var protocol = GetHeaderValue(requestHead.Headers, "Upgrade");
        if (string.IsNullOrWhiteSpace(protocol))
        {
            return UpgradeRequestValidationDecision.Rejected("HTTP Upgrade requires an Upgrade header.");
        }

        if (requestHead.Framing.Kind != Http1BodyKind.None)
        {
            return UpgradeRequestValidationDecision.Rejected("HTTP Upgrade request bodies are not supported in Phase 7.");
        }

        var isWebSocket = string.Equals(protocol, "websocket", StringComparison.OrdinalIgnoreCase);
        string? webSocketKey = null;
        if (isWebSocket)
        {
            if (!string.Equals(requestHead.Method, "GET", StringComparison.Ordinal))
            {
                return UpgradeRequestValidationDecision.Rejected("WebSocket Upgrade requires GET.");
            }

            webSocketKey = GetHeaderValue(requestHead.Headers, "Sec-WebSocket-Key");
            if (string.IsNullOrWhiteSpace(webSocketKey))
            {
                return UpgradeRequestValidationDecision.Rejected("WebSocket Upgrade requires Sec-WebSocket-Key.");
            }

            if (string.IsNullOrWhiteSpace(GetHeaderValue(requestHead.Headers, "Sec-WebSocket-Version")))
            {
                return UpgradeRequestValidationDecision.Rejected("WebSocket Upgrade requires Sec-WebSocket-Version.");
            }
        }

        return UpgradeRequestValidationDecision.Accepted(new UpgradeRequestInfo(protocol, isWebSocket, webSocketKey));
    }

    public static string? GetHeaderValue(IReadOnlyList<ProxyHeaderField> headers, string name)
    {
        ArgumentNullException.ThrowIfNull(headers);
        foreach (var header in headers)
        {
            if (string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return header.Value;
            }
        }

        return null;
    }

    public static bool IsManagedUpgradeHeader(string headerName)
    {
        return string.Equals(headerName, "Connection", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "Upgrade", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsUnsafeSwitchingProtocolsResponseHeader(string headerName)
    {
        return string.Equals(headerName, "Keep-Alive", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "Proxy-Authenticate", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "Proxy-Authorization", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "TE", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "Trailer", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasHeader(IReadOnlyList<ProxyHeaderField> headers, string name)
    {
        return headers.Any(header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
