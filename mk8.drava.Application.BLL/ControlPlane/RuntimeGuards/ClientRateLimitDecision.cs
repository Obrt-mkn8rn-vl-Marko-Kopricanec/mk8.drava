namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
public abstract record ClientRateLimitDecision
{
    private ClientRateLimitDecision()
    {
    }

    public static ClientRateLimitDecision Accepted { get; } = new AcceptedResult();
    public static ClientRateLimitDecision Rejected { get; } = new RejectedResult();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedResult : ClientRateLimitDecision;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedResult : ClientRateLimitDecision;
}
