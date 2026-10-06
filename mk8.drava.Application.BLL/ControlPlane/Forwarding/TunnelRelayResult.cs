namespace Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
public sealed record TunnelRelayResult
{
    private TunnelRelayResult(string closeReason, ProxyFailureKind failureKind, long bytesClientToUpstream, long bytesUpstreamToClient, TimeSpan duration)
    {
        CloseReason = closeReason;
        FailureKind = failureKind;
        BytesClientToUpstream = bytesClientToUpstream;
        BytesUpstreamToClient = bytesUpstreamToClient;
        Duration = duration;
    }

    public string CloseReason { get; }
    public ProxyFailureKind FailureKind { get; }
    public long BytesClientToUpstream { get; }
    public long BytesUpstreamToClient { get; }
    public TimeSpan Duration { get; }

    public static TunnelRelayResult Closed(long bytesClientToUpstream, long bytesUpstreamToClient, TimeSpan duration)
    {
        return Create(closeReason: "Closed", failureKind: ProxyFailureKind.None, bytesClientToUpstream, bytesUpstreamToClient, duration);
    }

    public static TunnelRelayResult Shutdown(long bytesClientToUpstream, long bytesUpstreamToClient, TimeSpan duration)
    {
        return Create(closeReason: "Shutdown", failureKind: ProxyFailureKind.None, bytesClientToUpstream, bytesUpstreamToClient, duration);
    }

    public static TunnelRelayResult IdleTimedOut(long bytesClientToUpstream, long bytesUpstreamToClient, TimeSpan duration)
    {
        return Create(closeReason: "IdleTimeout", failureKind: ProxyFailureKind.TunnelIdleTimeout, bytesClientToUpstream, bytesUpstreamToClient, duration);
    }

    public static TunnelRelayResult RelayFailed(long bytesClientToUpstream, long bytesUpstreamToClient, TimeSpan duration)
    {
        return Create(closeReason: "RelayFailure", failureKind: ProxyFailureKind.TunnelRelayFailure, bytesClientToUpstream, bytesUpstreamToClient, duration);
    }

    private static TunnelRelayResult Create(string closeReason, ProxyFailureKind failureKind, long bytesClientToUpstream, long bytesUpstreamToClient, TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytesClientToUpstream);
        ArgumentOutOfRangeException.ThrowIfNegative(bytesUpstreamToClient);
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Tunnel relay duration cannot be negative.");
        }

        return new TunnelRelayResult(closeReason, failureKind, bytesClientToUpstream, bytesUpstreamToClient, duration);
    }
}
