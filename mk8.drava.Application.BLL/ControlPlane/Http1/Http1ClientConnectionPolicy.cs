using Mk8.Drava.Application.BLL.ControlPlane.Headers;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public static class Http1ClientConnectionPolicy
{
    public static bool ShouldKeepOpen(Http1RequestHead requestHead)
    {
        ArgumentNullException.ThrowIfNull(requestHead);
        if (HopByHopHeaderPolicy.HasConnectionToken(requestHead.Headers, "close"))
        {
            return false;
        }

        return string.Equals(requestHead.Version, "HTTP/1.1", StringComparison.OrdinalIgnoreCase);
    }
}
