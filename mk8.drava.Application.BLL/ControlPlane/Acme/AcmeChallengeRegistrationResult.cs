namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public abstract record AcmeChallengeRegistrationResult
{
    private AcmeChallengeRegistrationResult()
    {
    }

    public static AcmeChallengeRegistrationResult Registered { get; } = new RegisteredResult();

    public static AcmeChallengeRegistrationResult Rejected(string reason)
    {
        return new RejectedResult(reason);
    }

    private sealed record RegisteredResult : AcmeChallengeRegistrationResult;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedResult : AcmeChallengeRegistrationResult
    {
        public RejectedResult(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("ACME challenge registration rejection reason is required.", nameof(reason));
            }

            Reason = reason;
        }

        public string Reason { get; }
    }
}
