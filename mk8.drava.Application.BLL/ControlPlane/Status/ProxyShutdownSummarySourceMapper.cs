using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyShutdownSummarySourceMapper
{
    public static ProxyShutdownSummarySource FromSources(bool isRunning, bool isShuttingDown, DateTimeOffset? shutdownStartedAtUtc, DateTimeOffset? shutdownDeadlineUtc)
    {
        return new ProxyShutdownSummarySource(isRunning, isShuttingDown, shutdownStartedAtUtc, shutdownDeadlineUtc);
    }
}
