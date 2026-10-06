namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public abstract record ProxyCacheResponseFramingEligibility
{
    private ProxyCacheResponseFramingEligibility()
    {
    }

    public static ProxyCacheResponseFramingEligibility Accept()
    {
        return Accepted.Instance;
    }

    public static ProxyCacheResponseFramingEligibility Reject(string reason)
    {
        return new Rejected(reason);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record Accepted : ProxyCacheResponseFramingEligibility
    {
        internal static Accepted Instance { get; } = new();

        private Accepted()
        {
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record Rejected : ProxyCacheResponseFramingEligibility
    {
        public Rejected(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Cache response framing rejection reason is required.", nameof(reason));
            }

            Reason = reason;
        }

        public string Reason { get; }
    }
}
