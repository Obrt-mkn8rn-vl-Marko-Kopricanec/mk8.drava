namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeConnectionLimits
{
    public RuntimeConnectionLimits(int MaxRequestsPerClientConnection, int MaxIdleUpstreamConnectionsPerUpstream, int MaxActiveUpgradedTunnels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxRequestsPerClientConnection);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxIdleUpstreamConnectionsPerUpstream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxActiveUpgradedTunnels);
        this.MaxRequestsPerClientConnection = MaxRequestsPerClientConnection;
        this.MaxIdleUpstreamConnectionsPerUpstream = MaxIdleUpstreamConnectionsPerUpstream;
        this.MaxActiveUpgradedTunnels = MaxActiveUpgradedTunnels;
    }

    public int MaxRequestsPerClientConnection { get; }
    public int MaxIdleUpstreamConnectionsPerUpstream { get; }
    public int MaxActiveUpgradedTunnels { get; }
}
