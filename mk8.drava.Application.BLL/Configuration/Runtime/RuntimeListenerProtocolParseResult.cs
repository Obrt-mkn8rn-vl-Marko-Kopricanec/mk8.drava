namespace Mk8.Drava.Application.BLL.Configuration;
public abstract record RuntimeListenerProtocolParseResult
{
    private RuntimeListenerProtocolParseResult()
    {
    }

    public static RuntimeListenerProtocolParseResult Rejected { get; } = new RejectedResult();

    public static RuntimeListenerProtocolParseResult Accepted(RuntimeListenerProtocols protocols)
    {
        return new AcceptedResult(protocols);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedResult : RuntimeListenerProtocolParseResult
    {
        public AcceptedResult(RuntimeListenerProtocols Protocols)
        {
            RuntimeHttp3CompatibilityFacts.ValidateProtocols(Protocols, nameof(Protocols));
            this.Protocols = Protocols;
        }

        public RuntimeListenerProtocols Protocols { get; }
    }

    private sealed record RejectedResult : RuntimeListenerProtocolParseResult;
}
