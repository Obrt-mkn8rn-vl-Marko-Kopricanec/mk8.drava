namespace Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
public static class ProxyFailureKindText
{
    public static string FromFailureKind(ProxyFailureKind failureKind)
    {
        return failureKind switch
        {
            ProxyFailureKind.None => nameof(ProxyFailureKind.None),
            ProxyFailureKind.ClientMalformedRequest => nameof(ProxyFailureKind.ClientMalformedRequest),
            ProxyFailureKind.ClientRequestHeadTimeout => nameof(ProxyFailureKind.ClientRequestHeadTimeout),
            ProxyFailureKind.ClientRequestBodyTimeout => nameof(ProxyFailureKind.ClientRequestBodyTimeout),
            ProxyFailureKind.ClientDisconnected => nameof(ProxyFailureKind.ClientDisconnected),
            ProxyFailureKind.UpstreamUnavailable => nameof(ProxyFailureKind.UpstreamUnavailable),
            ProxyFailureKind.UpstreamConnectTimeout => nameof(ProxyFailureKind.UpstreamConnectTimeout),
            ProxyFailureKind.UpstreamConnectFailed => nameof(ProxyFailureKind.UpstreamConnectFailed),
            ProxyFailureKind.UpstreamResponseHeadTimeout => nameof(ProxyFailureKind.UpstreamResponseHeadTimeout),
            ProxyFailureKind.UpstreamResponseBodyTimeout => nameof(ProxyFailureKind.UpstreamResponseBodyTimeout),
            ProxyFailureKind.UpstreamPrematureDisconnect => nameof(ProxyFailureKind.UpstreamPrematureDisconnect),
            ProxyFailureKind.UpstreamMalformedResponse => nameof(ProxyFailureKind.UpstreamMalformedResponse),
            ProxyFailureKind.NoMatchingRoute => nameof(ProxyFailureKind.NoMatchingRoute),
            ProxyFailureKind.NoHealthyUpstream => nameof(ProxyFailureKind.NoHealthyUpstream),
            ProxyFailureKind.TlsHandshakeFailed => nameof(ProxyFailureKind.TlsHandshakeFailed),
            ProxyFailureKind.TlsHandshakeTimeout => nameof(ProxyFailureKind.TlsHandshakeTimeout),
            ProxyFailureKind.UpgradeValidationFailed => nameof(ProxyFailureKind.UpgradeValidationFailed),
            ProxyFailureKind.UpgradeRejected => nameof(ProxyFailureKind.UpgradeRejected),
            ProxyFailureKind.RateLimited => nameof(ProxyFailureKind.RateLimited),
            ProxyFailureKind.UpgradeRateLimited => nameof(ProxyFailureKind.UpgradeRateLimited),
            ProxyFailureKind.AdmissionRejected => nameof(ProxyFailureKind.AdmissionRejected),
            ProxyFailureKind.RequestPayloadTooLarge => nameof(ProxyFailureKind.RequestPayloadTooLarge),
            ProxyFailureKind.ParserLimitExceeded => nameof(ProxyFailureKind.ParserLimitExceeded),
            ProxyFailureKind.Shutdown => nameof(ProxyFailureKind.Shutdown),
            ProxyFailureKind.TunnelIdleTimeout => nameof(ProxyFailureKind.TunnelIdleTimeout),
            ProxyFailureKind.TunnelRelayFailure => nameof(ProxyFailureKind.TunnelRelayFailure),
            ProxyFailureKind.InternalError => nameof(ProxyFailureKind.InternalError),
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind), failureKind, "Unknown proxy failure kind.")};
    }
}
