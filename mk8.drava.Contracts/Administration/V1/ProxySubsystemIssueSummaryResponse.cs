namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxySubsystemIssueSummaryResponse(DateTimeOffset TimestampUtc, string Category, string Reason, string? AffectedIdentity)
{
}
