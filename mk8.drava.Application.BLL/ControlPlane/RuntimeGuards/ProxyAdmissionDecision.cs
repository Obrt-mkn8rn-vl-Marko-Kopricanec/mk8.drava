namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
public abstract record ProxyAdmissionDecision
{
    private ProxyAdmissionDecision()
    {
    }

    public static ProxyAdmissionDecision Rejected { get; } = new RejectedResult();

    public static ProxyAdmissionDecision Accepted(AdmissionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return new AcceptedResult(lease);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedResult : ProxyAdmissionDecision
    {
        public AcceptedResult(AdmissionLease lease)
        {
            ArgumentNullException.ThrowIfNull(lease);
            Lease = lease;
        }

        public AdmissionLease Lease { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedResult : ProxyAdmissionDecision;
}
