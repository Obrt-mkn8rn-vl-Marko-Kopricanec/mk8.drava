namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ConfigLintResponse
{
    public ConfigLintResponse(bool succeeded, DateTimeOffset lintedAtUtc, ConfigLintSummaryResponse summary, IReadOnlyList<ConfigLintFindingResponse> findings, IReadOnlyList<ProxyConfigurationFileErrorResponse> validationErrors)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Succeeded = succeeded;
        LintedAtUtc = lintedAtUtc;
        Summary = summary;
        Findings = ApiResponseList.Copy(findings);
        ValidationErrors = ApiResponseList.Copy(validationErrors);
    }

    public bool Succeeded { get; }
    public DateTimeOffset LintedAtUtc { get; }
    public ConfigLintSummaryResponse Summary { get; }
    public IReadOnlyList<ConfigLintFindingResponse> Findings { get; }
    public IReadOnlyList<ProxyConfigurationFileErrorResponse> ValidationErrors { get; }
}
