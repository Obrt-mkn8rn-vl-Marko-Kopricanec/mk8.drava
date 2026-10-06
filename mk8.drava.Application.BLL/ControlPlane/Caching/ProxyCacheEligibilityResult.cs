namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public abstract record ProxyCacheEligibilityResult
{
    private ProxyCacheEligibilityResult()
    {
    }

    public static ProxyCacheEligibilityResult Accepted()
    {
        return AcceptedResult.Instance;
    }

    public static ProxyCacheEligibilityResult Rejected(string reason)
    {
        return new RejectedResult(reason);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedResult : ProxyCacheEligibilityResult
    {
        public static AcceptedResult Instance { get; } = new();

        private AcceptedResult()
        {
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedResult : ProxyCacheEligibilityResult
    {
        public RejectedResult(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Cache eligibility rejection reason is required.", nameof(reason));
            }

            Reason = reason;
        }

        public string Reason { get; }
    }
}
