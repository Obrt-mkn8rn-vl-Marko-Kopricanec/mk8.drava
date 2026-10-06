namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyCacheSubsystemSummaryResponse(bool Enabled, int EnabledRoutes, int EntryCount, long ApproximateBytes)
{
}
