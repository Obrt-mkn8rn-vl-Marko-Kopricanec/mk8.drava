using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Mk8.Drava.Application.INF.Observability;
public sealed class UpstreamHealthCheckLogger : IUpstreamHealthCheckEventSink
{
    private readonly ILogger<UpstreamHealthCheckLogger> _logger;
    public UpstreamHealthCheckLogger(ILogger<UpstreamHealthCheckLogger> logger)
    {
        _logger = logger;
    }

    public void Checked(string routeName, string upstreamName, string endpoint, string result, UpstreamHealthState state)
    {
        _logger.LogDebug("Health check for route {RouteName} upstream {UpstreamName} at {Endpoint} returned {Result}; state is {HealthState}", routeName, upstreamName, endpoint, result, state);
    }
}
