namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeLogPersistenceResponse(bool AccessLogEnabled, bool AdminAuditEnabled, long MaxFileBytes, int MaxFiles)
{
}
