using Mk8.Drava.Application.BLL.ControlPlane.Routing;
using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
public static class ProxyGeneratedFailurePolicy
{
    public const string PlainTextContentType = "text/plain";
    public static bool CanWriteFailureResponse(bool responseStarted, bool suppressGeneratedFailureResponse)
    {
        return !responseStarted && !suppressGeneratedFailureResponse;
    }

    public static ProxyGeneratedFailureResponse BuildFailureResponse(ForwardingResult.FailureResult failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var statusCode = failure.ResponseStatusCode ?? ProxyForwardingFailurePolicy.StatusCodeForFailure(failure.FailureKind);
        return BuildFailureResponse(statusCode, failure.FailureKind);
    }

    public static ProxyGeneratedFailureResponse BuildFailureResponse(ProxyFailureKind failureKind)
    {
        return BuildFailureResponse(ProxyForwardingFailurePolicy.StatusCodeForFailure(failureKind), failureKind);
    }

    private static ProxyGeneratedFailureResponse BuildFailureResponse(int statusCode, ProxyFailureKind failureKind)
    {
        return new ProxyGeneratedFailureResponse(statusCode, ProxyRouteActionPolicy.ReasonPhrase(statusCode), failureKind);
    }

    public static ProxyGeneratedFailureResponse BuildFailureResponse(int statusCode, string reasonPhrase, string body, ProxyFailureKind failureKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonPhrase);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        return new ProxyGeneratedFailureResponse(statusCode, reasonPhrase, body, failureKind);
    }

    public static ProxyGeneratedFailureResponse BuildFailureResponse(int statusCode, string body, ProxyFailureKind failureKind)
    {
        return BuildFailureResponse(statusCode, body, body, failureKind);
    }

    public static IReadOnlyList<ProxyHeaderField> BuildFramedResponseHeaders(ProxyGeneratedFailureResponse response, string requestId, int bodyByteLength)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentOutOfRangeException.ThrowIfNegative(bodyByteLength);
        return[new ProxyHeaderField("content-type", PlainTextContentType), new ProxyHeaderField("x-request-id", requestId), new ProxyHeaderField("content-length", bodyByteLength.ToString(System.Globalization.CultureInfo.InvariantCulture))];
    }
}
