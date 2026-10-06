namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeConnectionLimitsResponse(int MaxRequestsPerClientConnection, int MaxIdleUpstreamConnectionsPerUpstream, int MaxActiveUpgradedTunnels)
{
}
