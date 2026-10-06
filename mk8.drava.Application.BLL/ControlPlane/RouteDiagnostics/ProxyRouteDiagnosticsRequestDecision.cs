namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public abstract record ProxyRouteDiagnosticsRequestDecision
{
    private ProxyRouteDiagnosticsRequestDecision()
    {
    }

    public static ProxyRouteDiagnosticsRequestDecision Accepted(ProxyRouteDiagnosticsRequestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new AcceptedDecision(input);
    }

    public static ProxyRouteDiagnosticsRequestDecision Rejected(RouteMatchDryRunResult.FailedResult failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new RejectedDecision(failure);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedDecision : ProxyRouteDiagnosticsRequestDecision
    {
        public AcceptedDecision(ProxyRouteDiagnosticsRequestInput Input)
        {
            ArgumentNullException.ThrowIfNull(Input);
            this.Input = Input;
        }

        public ProxyRouteDiagnosticsRequestInput Input { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedDecision : ProxyRouteDiagnosticsRequestDecision
    {
        public RejectedDecision(RouteMatchDryRunResult.FailedResult Failure)
        {
            ArgumentNullException.ThrowIfNull(Failure);
            this.Failure = Failure;
        }

        public RouteMatchDryRunResult.FailedResult Failure { get; }
    }
}
