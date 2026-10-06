using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public abstract partial record ConfigLintResult
{
    private ConfigLintResult(DateTimeOffset lintedAtUtc, ConfigLintSummary summary, IReadOnlyList<ConfigLintFinding> findings, IReadOnlyList<ProxyConfigurationFileError> validationErrors)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(validationErrors);
        LintedAtUtc = lintedAtUtc;
        Summary = summary;
        Findings = ConfigLintList.Copy(findings);
        ValidationErrors = ConfigLintList.Copy(validationErrors);
    }

    public DateTimeOffset LintedAtUtc { get; }
    public ConfigLintSummary Summary { get; }
    public IReadOnlyList<ConfigLintFinding> Findings { get; }
    public IReadOnlyList<ProxyConfigurationFileError> ValidationErrors { get; }

    public static ConfigLintResult Completed(DateTimeOffset lintedAtUtc, IReadOnlyList<ConfigLintFinding> findings, IReadOnlyList<ProxyConfigurationFileError> validationErrors)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(validationErrors);
        var copiedFindings = ConfigLintList.Copy(findings);
        var copiedValidationErrors = ConfigLintList.Copy(validationErrors);
        var summary = new ConfigLintSummary(copiedFindings.Count(static finding => string.Equals(finding.Severity, "info", StringComparison.OrdinalIgnoreCase)), copiedFindings.Count(static finding => string.Equals(finding.Severity, "warning", StringComparison.OrdinalIgnoreCase)), copiedFindings.Count(static finding => string.Equals(finding.Severity, "error", StringComparison.OrdinalIgnoreCase)));
        return summary.Error == 0 ? new AcceptedResult(lintedAtUtc, summary, copiedFindings, copiedValidationErrors) : new RejectedResult(lintedAtUtc, summary, copiedFindings, copiedValidationErrors);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedResult : ConfigLintResult
    {
        internal AcceptedResult(DateTimeOffset lintedAtUtc, ConfigLintSummary summary, IReadOnlyList<ConfigLintFinding> findings, IReadOnlyList<ProxyConfigurationFileError> validationErrors) : base(lintedAtUtc, summary, findings, validationErrors)
        {
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedResult : ConfigLintResult
    {
        internal RejectedResult(DateTimeOffset lintedAtUtc, ConfigLintSummary summary, IReadOnlyList<ConfigLintFinding> findings, IReadOnlyList<ProxyConfigurationFileError> validationErrors) : base(lintedAtUtc, summary, findings, validationErrors)
        {
        }
    }
}
