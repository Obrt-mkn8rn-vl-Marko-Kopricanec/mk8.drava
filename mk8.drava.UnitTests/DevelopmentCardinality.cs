using Xunit;

namespace Mk8.Drava.UnitTests;

internal static class DevelopmentCardinality
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "HLQ005", Justification = "This is xUnit Assert.Single, which rejects zero or multiple entries. First would weaken the exact-cardinality proof.")]
    public static T RequireSingle<T>(IEnumerable<T> values) => Assert.Single(values);
}
