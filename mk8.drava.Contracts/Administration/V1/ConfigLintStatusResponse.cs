namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ConfigLintStatusResponse(bool Available, DateTimeOffset? LastActiveLintAtUtc, ConfigLintSummaryResponse? LastActiveLintSummary)
{
}
