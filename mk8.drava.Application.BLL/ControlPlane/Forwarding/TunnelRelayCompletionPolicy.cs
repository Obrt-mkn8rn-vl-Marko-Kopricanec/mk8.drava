namespace Mk8.Drava.Application.BLL.ControlPlane.Forwarding;

public static class TunnelRelayCompletionPolicy
{
    public static TunnelRelayResult Classify(long bytesClientToUpstream, long bytesUpstreamToClient, TimeSpan duration,
        bool idleTimedOut, bool relayFailed, bool shutdownRequested)
    {
        if (idleTimedOut) return TunnelRelayResult.IdleTimedOut(bytesClientToUpstream, bytesUpstreamToClient, duration);
        if (relayFailed) return TunnelRelayResult.RelayFailed(bytesClientToUpstream, bytesUpstreamToClient, duration);
        if (shutdownRequested) return TunnelRelayResult.Shutdown(bytesClientToUpstream, bytesUpstreamToClient, duration);
        return TunnelRelayResult.Closed(bytesClientToUpstream, bytesUpstreamToClient, duration);
    }
}
