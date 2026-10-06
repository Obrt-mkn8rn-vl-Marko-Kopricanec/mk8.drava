namespace Mk8.Drava.Application.BLL.ControlPlane.Headers;
public static class Http2HeaderPolicy
{
    public static bool IsAllowedRequestPseudoHeader(string headerName)
    {
        return string.Equals(headerName, ":method", StringComparison.Ordinal) || string.Equals(headerName, ":scheme", StringComparison.Ordinal) || string.Equals(headerName, ":authority", StringComparison.Ordinal) || string.Equals(headerName, ":path", StringComparison.Ordinal) || string.Equals(headerName, ":protocol", StringComparison.Ordinal);
    }

    public static bool IsForbiddenRequestHeader(string headerName, string headerValue)
    {
        if (string.Equals(headerName, "te", StringComparison.OrdinalIgnoreCase))
        {
            return !string.Equals(headerValue, "trailers", StringComparison.OrdinalIgnoreCase);
        }

        return HopByHopHeaderPolicy.IsHopByHopHeader(headerName);
    }

    public static bool IsManagedUpstreamRequestHeader(string headerName)
    {
        ArgumentNullException.ThrowIfNull(headerName);
        return headerName.StartsWith(':') || string.Equals(headerName, "Host", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "Connection", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "Content-Length", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "Upgrade", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "Keep-Alive", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "Proxy-Connection", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "X-Request-Id", StringComparison.OrdinalIgnoreCase);
    }
}
