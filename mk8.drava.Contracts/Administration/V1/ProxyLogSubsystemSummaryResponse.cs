namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyLogSubsystemSummaryResponse(bool AccessLogPersistenceEnabled, bool AdminAuditPersistenceEnabled, string State, string Reason)
{
}
