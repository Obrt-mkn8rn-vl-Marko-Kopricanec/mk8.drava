namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RouteMatchDryRunResponse
{
    public RouteMatchDryRunResponse(bool succeeded, DateTimeOffset evaluatedAtUtc, string? failureReason, string? noMatchReason, RouteMatchDryRunListenerResponse? listener, RouteMatchDryRunRouteResponse? route, string? configuredAction, string? effectiveAction, bool wouldProxy, int? generatedStatusCode, string? originalTarget, string? rewrittenTarget, RouteMatchDryRunUpstreamResponse? upstream, RouteMatchDryRunPolicyResponse cache, RouteMatchDryRunPolicyResponse retry, RouteMatchDryRunPolicyResponse circuitBreaker, IReadOnlyList<RouteMatchDryRunFindingResponse> findings)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(retry);
        ArgumentNullException.ThrowIfNull(circuitBreaker);
        Succeeded = succeeded;
        EvaluatedAtUtc = evaluatedAtUtc;
        FailureReason = failureReason;
        NoMatchReason = noMatchReason;
        Listener = listener;
        Route = route;
        ConfiguredAction = configuredAction;
        EffectiveAction = effectiveAction;
        WouldProxy = wouldProxy;
        GeneratedStatusCode = generatedStatusCode;
        OriginalTarget = originalTarget;
        RewrittenTarget = rewrittenTarget;
        Upstream = upstream;
        Cache = cache;
        Retry = retry;
        CircuitBreaker = circuitBreaker;
        Findings = ApiResponseList.Copy(findings);
    }

    public bool Succeeded { get; }
    public DateTimeOffset EvaluatedAtUtc { get; }
    public string? FailureReason { get; }
    public string? NoMatchReason { get; }
    public RouteMatchDryRunListenerResponse? Listener { get; }
    public RouteMatchDryRunRouteResponse? Route { get; }
    public string? ConfiguredAction { get; }
    public string? EffectiveAction { get; }
    public bool WouldProxy { get; }
    public int? GeneratedStatusCode { get; }
    public string? OriginalTarget { get; }
    public string? RewrittenTarget { get; }
    public RouteMatchDryRunUpstreamResponse? Upstream { get; }
    public RouteMatchDryRunPolicyResponse Cache { get; }
    public RouteMatchDryRunPolicyResponse Retry { get; }
    public RouteMatchDryRunPolicyResponse CircuitBreaker { get; }
    public IReadOnlyList<RouteMatchDryRunFindingResponse> Findings { get; }
}
