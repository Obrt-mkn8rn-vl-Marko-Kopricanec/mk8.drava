namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public abstract record CircuitBreakerAcquisitionResult
{
    private CircuitBreakerAcquisitionResult()
    {
    }

    public static CircuitBreakerAcquisitionResult Rejected { get; } = new RejectedResult();

    public static CircuitBreakerAcquisitionResult Accepted(CircuitBreakerLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return new AcceptedResult(lease);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedResult : CircuitBreakerAcquisitionResult
    {
        public AcceptedResult(CircuitBreakerLease lease)
        {
            ArgumentNullException.ThrowIfNull(lease);
            Lease = lease;
        }

        public CircuitBreakerLease Lease { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedResult : CircuitBreakerAcquisitionResult;
}
