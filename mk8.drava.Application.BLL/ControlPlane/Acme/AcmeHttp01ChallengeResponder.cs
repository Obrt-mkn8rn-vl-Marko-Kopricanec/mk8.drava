using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed class AcmeHttp01ChallengeResponder
{
    public const string ChallengePathPrefix = "/.well-known/acme-challenge/";
    private readonly AcmeChallengeStore _challengeStore;
    private readonly TimeProvider _timeProvider;
    public AcmeHttp01ChallengeResponder(AcmeChallengeStore challengeStore, TimeProvider timeProvider)
    {
        _challengeStore = challengeStore;
        _timeProvider = timeProvider;
    }

    public AcmeHttp01ChallengeResponseResult CreateResponse(Http1RequestHead requestHead)
    {
        ArgumentNullException.ThrowIfNull(requestHead);
        if (!requestHead.Path.StartsWith(ChallengePathPrefix, StringComparison.Ordinal))
        {
            return AcmeHttp01ChallengeResponseResult.NoMatch;
        }

        var token = requestHead.Path[ChallengePathPrefix.Length..];
        if (!string.Equals(requestHead.Method, "GET", StringComparison.Ordinal) || !AcmeChallengeStore.IsValidToken(token))
        {
            return AcmeHttp01ChallengeResponseResult.Handled(NotFound());
        }

        var lookup = _challengeStore.FindResponse(token, _timeProvider.GetUtcNow());
        if (lookup is not AcmeChallengeResponseLookupResult.FoundResult found)
        {
            return AcmeHttp01ChallengeResponseResult.Handled(NotFound());
        }

        return AcmeHttp01ChallengeResponseResult.Handled(new GeneratedRouteResponse(200, "OK", "text/plain", found.ResponseBody, []));
    }

    private static GeneratedRouteResponse NotFound()
    {
        return new GeneratedRouteResponse(404, "Not Found", "text/plain", "Not Found", []);
    }
}
