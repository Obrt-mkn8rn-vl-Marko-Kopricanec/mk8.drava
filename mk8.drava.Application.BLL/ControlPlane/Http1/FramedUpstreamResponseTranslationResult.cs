using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public abstract record FramedUpstreamResponseTranslationResult
{
    private FramedUpstreamResponseTranslationResult()
    {
    }

    public static FramedUpstreamResponseTranslationResult Accepted(Http1ResponseHead responseHead)
    {
        ArgumentNullException.ThrowIfNull(responseHead);
        return new AcceptedResult(responseHead);
    }

    public static FramedUpstreamResponseTranslationResult Rejected(string reason)
    {
        return new RejectedResult(reason);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AcceptedResult : FramedUpstreamResponseTranslationResult
    {
        public AcceptedResult(Http1ResponseHead responseHead)
        {
            ArgumentNullException.ThrowIfNull(responseHead);
            ResponseHead = responseHead;
        }

        public Http1ResponseHead ResponseHead { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record RejectedResult : FramedUpstreamResponseTranslationResult
    {
        public RejectedResult(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Upstream response translation rejection reason is required.", nameof(reason));
            }

            Reason = reason;
        }

        public string Reason { get; }
    }
}
