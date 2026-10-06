namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeObservabilityResponse(bool AccessLogEnabled, int RecentDiagnosticsCapacity, RuntimeLogPersistenceResponse LogPersistence)
{
}
