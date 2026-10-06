namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeObservabilityFacts
{
    private const int MinimumDiagnosticsCapacity = 1;
    private const int MaximumDiagnosticsCapacity = 10_000;
    public static void ValidateRecentDiagnosticsCapacity(int recentDiagnosticsCapacity)
    {
        if (recentDiagnosticsCapacity is < MinimumDiagnosticsCapacity or > MaximumDiagnosticsCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(recentDiagnosticsCapacity));
        }
    }
}
