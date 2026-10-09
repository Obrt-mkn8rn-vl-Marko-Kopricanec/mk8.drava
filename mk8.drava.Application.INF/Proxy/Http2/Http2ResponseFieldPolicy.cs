using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;

namespace Mk8.Drava.Application.INF.Proxy.Http2;

internal static class Http2ResponseFieldPolicy
{
    public static void Validate(ProxyHeaderField field)
    {
        if (!FramedResponseFieldPolicy.IsValid(field))
            throw new Http2UpstreamProtocolException("Malformed HTTP/2 response field.");
    }
}
