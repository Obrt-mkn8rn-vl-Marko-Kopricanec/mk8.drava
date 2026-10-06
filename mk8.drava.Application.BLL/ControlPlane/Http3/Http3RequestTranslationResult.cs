using Mk8.Drava.Application.BLL.ControlPlane.Http1;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public abstract record Http3RequestTranslationResult
{
    private Http3RequestTranslationResult()
    {
    }

    public static Http3RequestTranslationResult Accepted(Http1RequestHead requestHead)
    {
        return new AcceptedResult(requestHead);
    }

    public static Http3RequestTranslationResult Rejected(string reason)
    {
        return new RejectedResult(reason);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedResult : Http3RequestTranslationResult
    {
        public AcceptedResult(Http1RequestHead requestHead)
        {
            ArgumentNullException.ThrowIfNull(requestHead);
            RequestHead = requestHead;
        }

        public Http1RequestHead RequestHead { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedResult : Http3RequestTranslationResult
    {
        public RejectedResult(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("HTTP/3 request rejection reason is required.", nameof(reason));
            }

            Reason = reason;
        }

        public string Reason { get; }
    }
}
