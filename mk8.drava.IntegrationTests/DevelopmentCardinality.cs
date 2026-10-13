using Xunit;

namespace Mk8.Drava.IntegrationTests;

internal static class DevelopmentCardinality
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "HLQ005", Justification = "This is xUnit Assert.Single, which rejects zero or multiple entries. Replacing the framework assertion with First would weaken the required exact-cardinality proof.")]
    public static void RequireSingle<T>(IEnumerable<T> values) => Assert.Single(values);
}
