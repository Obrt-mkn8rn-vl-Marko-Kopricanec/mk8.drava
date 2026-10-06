namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyUpstreamSubsystemSummaryResponse(int Total, int Healthy, int Unhealthy, int UnknownHealth, int HealthChecksEnabled)
{
}
