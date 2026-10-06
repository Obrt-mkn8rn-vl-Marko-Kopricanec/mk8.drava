using Mk8.Drava.Application.BLL.ControlPlane.Routing;
using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
public sealed record ProxyGeneratedFailureResponse(int StatusCode, string ReasonPhrase, string Body, ProxyFailureKind FailureKind)
{
    public ProxyGeneratedFailureResponse(int statusCode, string reasonPhrase, ProxyFailureKind failureKind) : this(statusCode, reasonPhrase, reasonPhrase, failureKind)
    {
    }

    public ForwardingResult ToForwardingResult()
    {
        return ForwardingResult.Failure(responseStarted: true, responseStatusCode: StatusCode, failureKind: FailureKind);
    }
}
