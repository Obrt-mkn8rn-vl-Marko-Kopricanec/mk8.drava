using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using System.Buffers.Binary;
using Mk8.Drava.Application.INF.Proxy.Forwarding;

namespace Mk8.Drava.Application.INF.Proxy.Http2;
internal sealed class Http2UpstreamProtocolException : IOException
{
    public Http2UpstreamProtocolException(string message) : base(message)
    {
    }

    public Http2UpstreamProtocolException() : base()
    {
    }

    public Http2UpstreamProtocolException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    public Http2UpstreamProtocolException(string? message, int hresult) : base(message, hresult)
    {
    }
}
