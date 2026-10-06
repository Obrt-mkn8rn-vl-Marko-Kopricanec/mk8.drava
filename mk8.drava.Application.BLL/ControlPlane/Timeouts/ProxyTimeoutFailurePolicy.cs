using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;

namespace Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
public static class ProxyTimeoutFailurePolicy
{
    public static ProxyTimeoutFailure ClassifyForwardingTimeout(ProxyTimeoutKind timeoutKind, bool responseStarted)
    {
        return new ProxyTimeoutFailure(ResponseStatusCodeForForwardingTimeout(timeoutKind, responseStarted), FailureKindForForwardingTimeout(timeoutKind));
    }

    private static int? ResponseStatusCodeForForwardingTimeout(ProxyTimeoutKind timeoutKind, bool responseStarted)
    {
        if (responseStarted)
        {
            return null;
        }

        return timeoutKind switch
        {
            ProxyTimeoutKind.ClientRequestBodyIdle => 408,
            ProxyTimeoutKind.UpstreamConnect => 504,
            ProxyTimeoutKind.UpstreamResponseHead => 504,
            _ => null
        };
    }

    private static ProxyFailureKind FailureKindForForwardingTimeout(ProxyTimeoutKind timeoutKind)
    {
        return timeoutKind switch
        {
            ProxyTimeoutKind.ClientRequestBodyIdle => ProxyFailureKind.ClientRequestBodyTimeout,
            ProxyTimeoutKind.UpstreamConnect => ProxyFailureKind.UpstreamConnectTimeout,
            ProxyTimeoutKind.UpstreamResponseHead => ProxyFailureKind.UpstreamResponseHeadTimeout,
            ProxyTimeoutKind.UpstreamResponseBodyIdle => ProxyFailureKind.UpstreamResponseBodyTimeout,
            ProxyTimeoutKind.DownstreamWrite => ProxyFailureKind.ClientDisconnected,
            _ => ProxyFailureKind.InternalError
        };
    }
}
