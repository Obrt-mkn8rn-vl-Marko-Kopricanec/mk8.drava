namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyConnectionOptions
{
    public int MaxRequestsPerClientConnection { get; init; } = 100;
    public int MaxIdleUpstreamConnectionsPerUpstream { get; init; } = 16;
    public int MaxActiveUpgradedTunnels { get; init; } = 1024;
}
