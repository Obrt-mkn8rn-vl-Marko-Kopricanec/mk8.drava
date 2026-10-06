using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyLimitConfigurationSummarySource
{
    public ProxyLimitConfigurationSummarySource(int MaxActiveClientConnections, int MaxConcurrentTlsHandshakes, int RequestsPerMinutePerIp)
    {
        ProxyStatusFacts.RequireNonNegative(MaxActiveClientConnections, nameof(MaxActiveClientConnections));
        ProxyStatusFacts.RequireNonNegative(MaxConcurrentTlsHandshakes, nameof(MaxConcurrentTlsHandshakes));
        ProxyStatusFacts.RequireNonNegative(RequestsPerMinutePerIp, nameof(RequestsPerMinutePerIp));
        this.MaxActiveClientConnections = MaxActiveClientConnections;
        this.MaxConcurrentTlsHandshakes = MaxConcurrentTlsHandshakes;
        this.RequestsPerMinutePerIp = RequestsPerMinutePerIp;
    }

    public int MaxActiveClientConnections { get; }
    public int MaxConcurrentTlsHandshakes { get; }
    public int RequestsPerMinutePerIp { get; }
}
