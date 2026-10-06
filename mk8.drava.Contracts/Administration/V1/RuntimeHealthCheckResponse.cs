namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeHealthCheckResponse(bool Enabled, string Path, TimeSpan Interval, TimeSpan Timeout, int HealthyThreshold, int UnhealthyThreshold)
{
}
