namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public abstract record Http1TransferEncodingAnalysisResult
{
    private Http1TransferEncodingAnalysisResult()
    {
    }

    public static Http1TransferEncodingAnalysisResult Accepted { get; } = new AcceptedResult();

    public static Http1TransferEncodingAnalysisResult Reject(Http1ParseError error)
    {
        if (error == Http1ParseError.None)
        {
            throw new ArgumentException("Transfer-Encoding rejection requires a parse error.", nameof(error));
        }

        return new Rejected(error);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record Rejected(Http1ParseError Error) : Http1TransferEncodingAnalysisResult;
    private sealed record AcceptedResult : Http1TransferEncodingAnalysisResult;
}
