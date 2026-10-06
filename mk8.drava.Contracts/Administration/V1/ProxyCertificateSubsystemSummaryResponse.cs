namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyCertificateSubsystemSummaryResponse(int Configured, int Loaded, int MissingReferences, int Expired, int NotYetValid, int ExpiringSoon, ProxySubsystemIssueSummaryResponse? LastIssue)
{
}
