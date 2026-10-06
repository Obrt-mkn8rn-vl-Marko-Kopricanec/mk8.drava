namespace Mk8.Drava.Application.BLL.Http;
public abstract record ProxyRequestApplicationMethodDecision
{
    private ProxyRequestApplicationMethodDecision()
    {
    }

    public static ProxyRequestApplicationMethodDecision Supported { get; } = new SupportedDecision();

    public static ProxyRequestApplicationMethodDecision Rejected(string reason)
    {
        return new RejectedDecision(reason);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedDecision : ProxyRequestApplicationMethodDecision
    {
        public RejectedDecision(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Request method rejection reason is required.", nameof(reason));
            }

            Reason = reason;
        }

        public string Reason { get; }
    }

    private sealed record SupportedDecision : ProxyRequestApplicationMethodDecision;
}
