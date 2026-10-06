namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public abstract record AcmeChallengeResponseLookupResult
{
    private AcmeChallengeResponseLookupResult()
    {
    }

    public static AcmeChallengeResponseLookupResult Missing { get; } = new MissingResult();

    public static AcmeChallengeResponseLookupResult Found(string responseBody)
    {
        return new FoundResult(responseBody);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record FoundResult : AcmeChallengeResponseLookupResult
    {
        public FoundResult(string responseBody)
        {
            if (string.IsNullOrEmpty(responseBody))
            {
                throw new ArgumentException("ACME challenge response body is required.", nameof(responseBody));
            }

            ResponseBody = responseBody;
        }

        public string ResponseBody { get; }
    }

    private sealed record MissingResult : AcmeChallengeResponseLookupResult;
}
