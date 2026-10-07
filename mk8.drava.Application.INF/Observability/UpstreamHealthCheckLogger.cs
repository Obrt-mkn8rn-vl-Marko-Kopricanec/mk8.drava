using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Mk8.Drava.Application.INF.Observability;
public sealed partial class UpstreamHealthCheckLogger : IUpstreamHealthCheckEventSink
{
    private readonly ILogger<UpstreamHealthCheckLogger> _logger;
    public UpstreamHealthCheckLogger(ILogger<UpstreamHealthCheckLogger> logger)
    {
        _logger = logger;
    }

    public void Checked(string routeName, string upstreamName, string endpoint, string result, UpstreamHealthState state)
    {
        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
        {
            LogHealthCheckForRouteUpstream10012(_logger, routeName, upstreamName, endpoint, result, state, null);
        }
    }
}
