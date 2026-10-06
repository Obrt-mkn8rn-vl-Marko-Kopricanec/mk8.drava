using Mk8.Drava.Application.BLL.ControlPlane.Headers;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public static class Http1ManagedHeaderPolicy
{
    public static bool IsManagedFramingHeader(string headerName)
    {
        return string.Equals(headerName, "Content-Length", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "Connection", StringComparison.OrdinalIgnoreCase) || string.Equals(headerName, "X-Request-Id", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsManagedStoredResponseHeader(string headerName)
    {
        return string.Equals(headerName, "Age", StringComparison.OrdinalIgnoreCase) || IsManagedFramingHeader(headerName) || HopByHopHeaderPolicy.IsHopByHopHeader(headerName);
    }
}
