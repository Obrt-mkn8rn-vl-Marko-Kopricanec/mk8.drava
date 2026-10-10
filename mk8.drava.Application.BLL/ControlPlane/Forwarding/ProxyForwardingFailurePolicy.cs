namespace Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
public static class ProxyForwardingFailurePolicy
{
    public static ProxyFailureKind ClassifyConnectionFailure(bool responseStarted)
    {
        return responseStarted ? ProxyFailureKind.UpstreamPrematureDisconnect : ProxyFailureKind.UpstreamConnectFailed;
    }

    public static ProxyFailureKind ClassifyDisconnectingProtocolFailure(bool connectionFailed, bool responseStarted)
    {
        if (responseStarted)
        {
            return ProxyFailureKind.UpstreamPrematureDisconnect;
        }

        return connectionFailed ? ProxyFailureKind.UpstreamConnectFailed : ProxyFailureKind.UpstreamMalformedResponse;
    }

    public static bool IsCircuitFailure(ProxyFailureKind failureKind)
    {
        return failureKind is ProxyFailureKind.UpstreamConnectFailed or ProxyFailureKind.UpstreamConnectTimeout or ProxyFailureKind.UpstreamResponseHeadTimeout;
    }

    public static string CircuitFailureReason(ProxyFailureKind failureKind)
    {
        return failureKind switch
        {
            ProxyFailureKind.UpstreamConnectFailed => "connect_failure",
            ProxyFailureKind.UpstreamConnectTimeout => "connect_timeout",
            ProxyFailureKind.UpstreamResponseHeadTimeout => "response_head_timeout",
            _ => "other",
        };
    }

    public static int StatusCodeForFailure(ProxyFailureKind failureKind)
    {
        return failureKind switch
        {
            ProxyFailureKind.ClientRequestBodyTimeout => 408,
            ProxyFailureKind.UpstreamConnectTimeout => 504,
            ProxyFailureKind.UpstreamResponseHeadTimeout => 504,
            ProxyFailureKind.NoHealthyUpstream => 503,
            ProxyFailureKind.UpgradeRejected => 503,
            ProxyFailureKind.RequestPayloadTooLarge => 413,
            ProxyFailureKind.ClientMalformedRequest => 400,
            _ => 502,
        };
    }

    public static int? ResponseStatusCodeForFailure(bool responseStarted, ProxyFailureKind failureKind)
    {
        return responseStarted ? null : StatusCodeForFailure(failureKind);
    }
}
