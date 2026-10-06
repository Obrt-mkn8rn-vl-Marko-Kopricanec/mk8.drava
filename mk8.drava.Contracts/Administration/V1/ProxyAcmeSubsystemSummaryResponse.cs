namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyAcmeSubsystemSummaryResponse(bool Enabled, int Configured, int Active, int Failed, int RenewalBackoff, ProxySubsystemIssueSummaryResponse? LastIssue)
{
}
