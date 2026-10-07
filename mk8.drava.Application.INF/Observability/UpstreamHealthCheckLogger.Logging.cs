namespace Mk8.Drava.Application.INF.Observability;

public sealed partial class UpstreamHealthCheckLogger
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10012, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Health check for route {RouteName} upstream {UpstreamName} at {Endpoint} returned {Result}; state is {HealthState}", SkipEnabledCheck = true)]
    private static partial void LogHealthCheckForRouteUpstream10012(global::Microsoft.Extensions.Logging.ILogger logger, string routeName, string upstreamName, string endpoint, string result, global::Mk8.Drava.Application.BLL.ControlPlane.HealthChecks.UpstreamHealthState healthState, global::System.Exception? exception);
}
