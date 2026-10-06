namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyShutdownSubsystemSummary
{
    public ProxyShutdownSubsystemSummary(bool IsRunning, bool IsShuttingDown, DateTimeOffset? ShutdownStartedAtUtc, DateTimeOffset? ShutdownDeadlineUtc)
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
    public static ProxyShutdownSubsystemSummary Unknown { get; } = new(false, false, null, null);
}
