namespace Mk8.Drava.Application.BLL.Configuration;
public abstract record RuntimeHttp3EnablementParseResult
{
    private RuntimeHttp3EnablementParseResult()
    {
    }

    public static RuntimeHttp3EnablementParseResult Accepted(RuntimeHttp3Enablement enablement, bool explicitlyConfigured)
    {
        return new AcceptedResult(enablement, explicitlyConfigured);
    }

    public static RuntimeHttp3EnablementParseResult Rejected(bool explicitlyConfigured)
    {
        return new RejectedResult(explicitlyConfigured);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedResult : RuntimeHttp3EnablementParseResult
    {
        public AcceptedResult(RuntimeHttp3Enablement Enablement, bool ExplicitlyConfigured)
        {
            RuntimeHttp3CompatibilityFacts.ValidateEnablement(Enablement, nameof(Enablement));
            this.Enablement = Enablement;
            this.ExplicitlyConfigured = ExplicitlyConfigured;
        }

        public RuntimeHttp3Enablement Enablement { get; }
        public bool ExplicitlyConfigured { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedResult(bool ExplicitlyConfigured) : RuntimeHttp3EnablementParseResult;
}
