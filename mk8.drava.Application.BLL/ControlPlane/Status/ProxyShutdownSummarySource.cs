using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyShutdownSummarySource
{
    public ProxyShutdownSummarySource(bool IsRunning, bool IsShuttingDown, DateTimeOffset? ShutdownStartedAtUtc, DateTimeOffset? ShutdownDeadlineUtc)
    {
        ProxyStatusFacts.RequireShutdownWindow(IsShuttingDown, ShutdownStartedAtUtc, nameof(ShutdownStartedAtUtc), ShutdownDeadlineUtc, nameof(ShutdownDeadlineUtc));
        this.IsRunning = IsRunning;
        this.IsShuttingDown = IsShuttingDown;
        this.ShutdownStartedAtUtc = ShutdownStartedAtUtc;
        this.ShutdownDeadlineUtc = ShutdownDeadlineUtc;
    }

    public bool IsRunning { get; }
    public bool IsShuttingDown { get; }
    public DateTimeOffset? ShutdownStartedAtUtc { get; }
    public DateTimeOffset? ShutdownDeadlineUtc { get; }
}
