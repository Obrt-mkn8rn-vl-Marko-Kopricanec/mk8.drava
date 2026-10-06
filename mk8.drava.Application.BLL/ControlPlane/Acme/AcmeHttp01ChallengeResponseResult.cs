using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public abstract record AcmeHttp01ChallengeResponseResult
{
    private AcmeHttp01ChallengeResponseResult()
    {
    }

    public static AcmeHttp01ChallengeResponseResult NoMatch { get; } = new NoMatchResult();

    public static AcmeHttp01ChallengeResponseResult Handled(GeneratedRouteResponse response)
    {
        return new HandledResult(response);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record NoMatchResult : AcmeHttp01ChallengeResponseResult;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record HandledResult : AcmeHttp01ChallengeResponseResult
    {
        public HandledResult(GeneratedRouteResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            Response = response;
        }

        public GeneratedRouteResponse Response { get; }
    }
}
