namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public sealed record ConfigLintStatus
{
    public static ConfigLintStatus Empty { get; } = new(available: true, lastActiveLintAtUtc: null, lastActiveLintSummary: null);

    private ConfigLintStatus(bool available, DateTimeOffset? lastActiveLintAtUtc, ConfigLintSummary? lastActiveLintSummary)
    {
        Available = available;
        LastActiveLintAtUtc = lastActiveLintAtUtc;
        LastActiveLintSummary = lastActiveLintSummary;
    }

    public bool Available { get; }
    public DateTimeOffset? LastActiveLintAtUtc { get; }
    public ConfigLintSummary? LastActiveLintSummary { get; }

    public static ConfigLintStatus Completed(DateTimeOffset lintedAtUtc, ConfigLintSummary summary)
    {
        return new ConfigLintStatus(available: true, lastActiveLintAtUtc: lintedAtUtc, lastActiveLintSummary: summary);
    }
}
