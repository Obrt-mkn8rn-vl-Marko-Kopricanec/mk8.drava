namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public abstract record ProxyTunnelAdmissionDecision
{
    private ProxyTunnelAdmissionDecision()
    {
    }

    public static ProxyTunnelAdmissionDecision Accepted { get; } = new AcceptedResult();
    public static ProxyTunnelAdmissionDecision Rejected { get; } = new RejectedResult();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedResult : ProxyTunnelAdmissionDecision;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedResult : ProxyTunnelAdmissionDecision;
}
