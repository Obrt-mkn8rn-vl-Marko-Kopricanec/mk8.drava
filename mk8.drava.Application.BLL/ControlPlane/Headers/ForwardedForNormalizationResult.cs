namespace Mk8.Drava.Application.BLL.ControlPlane.Headers;
public abstract record ForwardedForNormalizationResult
{
    private ForwardedForNormalizationResult()
    {
    }

    public static ForwardedForNormalizationResult Missing { get; } = new MissingResult();

    public static ForwardedForNormalizationResult Normalized(string clientAddress)
    {
        return new NormalizedResult(clientAddress);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record NormalizedResult : ForwardedForNormalizationResult
    {
        public NormalizedResult(string clientAddress)
        {
            if (string.IsNullOrWhiteSpace(clientAddress))
            {
                throw new ArgumentException("Forwarded client address is required.", nameof(clientAddress));
            }

            ClientAddress = clientAddress;
        }

        public string ClientAddress { get; }
    }

    private sealed record MissingResult : ForwardedForNormalizationResult;
}
