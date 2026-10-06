namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public abstract record ProxyCacheStorageEligibilityResult
{
    private ProxyCacheStorageEligibilityResult()
    {
    }

    public static ProxyCacheStorageEligibilityResult Accepted(TimeSpan ttl)
    {
        return new AcceptedResult(ttl);
    }

    public static ProxyCacheStorageEligibilityResult Rejected(string reason)
    {
        return new RejectedResult(reason);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedResult : ProxyCacheStorageEligibilityResult
    {
        public AcceptedResult(TimeSpan ttl)
        {
            if (ttl <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(ttl), "Cache storage TTL must be positive.");
            }

            Ttl = ttl;
        }

        public TimeSpan Ttl { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedResult : ProxyCacheStorageEligibilityResult
    {
        public RejectedResult(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Cache storage rejection reason is required.", nameof(reason));
            }

            Reason = reason;
        }

        public string Reason { get; }
    }
}
