namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyShutdownSubsystemSummaryResponse(bool IsRunning, bool IsShuttingDown, DateTimeOffset? ShutdownStartedAtUtc, DateTimeOffset? ShutdownDeadlineUtc)
{
}
