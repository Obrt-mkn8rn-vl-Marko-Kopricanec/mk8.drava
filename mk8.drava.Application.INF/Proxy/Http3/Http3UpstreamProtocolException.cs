using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
#pragma warning disable CA1416
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using System.Globalization;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.INF.Proxy.Forwarding;

namespace Mk8.Drava.Application.INF.Proxy.Http3;
internal sealed class Http3UpstreamProtocolException : IOException
{
    public Http3UpstreamProtocolException(string message) : base(message)
    {
    }

    public Http3UpstreamProtocolException(string message, Http3UpstreamFailureKind failureKind) : base(message)
    {
        FailureKind = failureKind;
    }

    public Http3UpstreamProtocolException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public Http3UpstreamProtocolException(string message, Http3UpstreamFailureKind failureKind, Exception innerException) : base(message, innerException)
    {
        FailureKind = failureKind;
    }

    public Http3UpstreamProtocolException() : base()
    {
    }

    public Http3UpstreamProtocolException(string? message, int hresult) : base(message, hresult)
    {
    }

    public Http3UpstreamFailureKind FailureKind { get; } = Http3UpstreamFailureKind.ProtocolError;
}
