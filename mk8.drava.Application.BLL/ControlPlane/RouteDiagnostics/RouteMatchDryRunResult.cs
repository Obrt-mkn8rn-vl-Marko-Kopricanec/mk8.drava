namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public abstract partial record RouteMatchDryRunResult
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MatchedRouteResult : RouteMatchDryRunResult
    {
        internal MatchedRouteResult(DateTimeOffset evaluatedAtUtc, string? noMatchReason, RouteMatchDryRunListener listener, RouteMatchDryRunRoute route, string configuredAction, string effectiveAction, bool wouldProxy, int? generatedStatusCode, string originalTarget, string rewrittenTarget, RouteMatchDryRunUpstream? upstream, RouteMatchDryRunPolicy cache, RouteMatchDryRunPolicy retry, RouteMatchDryRunPolicy circuitBreaker, IReadOnlyList<RouteMatchDryRunFinding> findings) : base(evaluatedAtUtc, cache, retry, circuitBreaker, findings)
        {
            ArgumentNullException.ThrowIfNull(listener);
            ArgumentNullException.ThrowIfNull(route);
            ArgumentException.ThrowIfNullOrWhiteSpace(configuredAction);
            ArgumentException.ThrowIfNullOrWhiteSpace(effectiveAction);
            ArgumentException.ThrowIfNullOrWhiteSpace(originalTarget);
            ArgumentException.ThrowIfNullOrWhiteSpace(rewrittenTarget);
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
        }

        public string? NoMatchReason { get; }
        public RouteMatchDryRunListener Listener { get; }
        public RouteMatchDryRunRoute Route { get; }
        public string ConfiguredAction { get; }
        public string EffectiveAction { get; }
        public bool WouldProxy { get; }
        public int? GeneratedStatusCode { get; }
        public string OriginalTarget { get; }
        public string RewrittenTarget { get; }
        public RouteMatchDryRunUpstream? Upstream { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record FailedResult : RouteMatchDryRunResult
    {
        internal FailedResult(DateTimeOffset evaluatedAtUtc, string failureReason, RouteMatchDryRunPolicy cache, RouteMatchDryRunPolicy retry, RouteMatchDryRunPolicy circuitBreaker, IReadOnlyList<RouteMatchDryRunFinding> findings) : base(evaluatedAtUtc, cache, retry, circuitBreaker, findings)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
            FailureReason = failureReason;
        }

        public string FailureReason { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record NoMatchingListenerResult : RouteMatchDryRunResult
    {
        internal NoMatchingListenerResult(DateTimeOffset evaluatedAtUtc, string noMatchReason, string originalTarget, RouteMatchDryRunPolicy cache, RouteMatchDryRunPolicy retry, RouteMatchDryRunPolicy circuitBreaker, IReadOnlyList<RouteMatchDryRunFinding> findings) : base(evaluatedAtUtc, cache, retry, circuitBreaker, findings)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(noMatchReason);
            ArgumentException.ThrowIfNullOrWhiteSpace(originalTarget);
            NoMatchReason = noMatchReason;
            OriginalTarget = originalTarget;
        }

        public string NoMatchReason { get; }
        public string OriginalTarget { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record NoMatchingRouteResult : RouteMatchDryRunResult
    {
        internal NoMatchingRouteResult(DateTimeOffset evaluatedAtUtc, string noMatchReason, RouteMatchDryRunListener listener, string originalTarget, RouteMatchDryRunPolicy cache, RouteMatchDryRunPolicy retry, RouteMatchDryRunPolicy circuitBreaker, IReadOnlyList<RouteMatchDryRunFinding> findings) : base(evaluatedAtUtc, cache, retry, circuitBreaker, findings)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(noMatchReason);
            ArgumentNullException.ThrowIfNull(listener);
            ArgumentException.ThrowIfNullOrWhiteSpace(originalTarget);
            NoMatchReason = noMatchReason;
            Listener = listener;
            OriginalTarget = originalTarget;
        }

        public string NoMatchReason { get; }
        public RouteMatchDryRunListener Listener { get; }
        public string OriginalTarget { get; }
    }

    private RouteMatchDryRunResult(DateTimeOffset evaluatedAtUtc, RouteMatchDryRunPolicy cache, RouteMatchDryRunPolicy retry, RouteMatchDryRunPolicy circuitBreaker, IReadOnlyList<RouteMatchDryRunFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(retry);
        ArgumentNullException.ThrowIfNull(circuitBreaker);
        ArgumentNullException.ThrowIfNull(findings);
        EvaluatedAtUtc = evaluatedAtUtc;
        Cache = cache;
        Retry = retry;
        CircuitBreaker = circuitBreaker;
        Findings = new List<RouteMatchDryRunFinding>(findings).AsReadOnly();
    }

    public DateTimeOffset EvaluatedAtUtc { get; }
    public RouteMatchDryRunPolicy Cache { get; }
    public RouteMatchDryRunPolicy Retry { get; }
    public RouteMatchDryRunPolicy CircuitBreaker { get; }
    public IReadOnlyList<RouteMatchDryRunFinding> Findings { get; }

    public static FailedResult Failed(DateTimeOffset evaluatedAtUtc, string reason, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new FailedResult(evaluatedAtUtc, reason, ProxyRouteDiagnosticsPolicyExplainer.Disabled(reason), ProxyRouteDiagnosticsPolicyExplainer.Disabled(reason), ProxyRouteDiagnosticsPolicyExplainer.Disabled(reason), [new RouteMatchDryRunFinding("error", reason, message)]);
    }

    public static NoMatchingListenerResult NoMatchingListener(DateTimeOffset evaluatedAtUtc, string originalTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalTarget);
        return new NoMatchingListenerResult(evaluatedAtUtc, "no_matching_listener", originalTarget, ProxyRouteDiagnosticsPolicyExplainer.Disabled("no_route"), ProxyRouteDiagnosticsPolicyExplainer.Disabled("no_route"), ProxyRouteDiagnosticsPolicyExplainer.Disabled("no_route"), [new RouteMatchDryRunFinding("warning", "no_matching_listener", "No enabled listener matches the supplied scheme, port, or listener identity.")]);
    }

    public static NoMatchingRouteResult NoMatchingRoute(DateTimeOffset evaluatedAtUtc, RouteMatchDryRunListener listener, string originalTarget)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalTarget);
        return new NoMatchingRouteResult(evaluatedAtUtc, "no_matching_route", listener, originalTarget, ProxyRouteDiagnosticsPolicyExplainer.Disabled("no_route"), ProxyRouteDiagnosticsPolicyExplainer.Disabled("no_route"), ProxyRouteDiagnosticsPolicyExplainer.Disabled("no_route"), [new RouteMatchDryRunFinding("info", "no_matching_route", "No configured route matched the supplied host and path.")]);
    }

    public static MatchedRouteResult MatchedRoute(DateTimeOffset evaluatedAtUtc, string? noMatchReason, RouteMatchDryRunListener listener, RouteMatchDryRunRoute route, string configuredAction, string effectiveAction, bool wouldProxy, int? generatedStatusCode, string originalTarget, string rewrittenTarget, RouteMatchDryRunUpstream? upstream, RouteMatchDryRunPolicy cache, RouteMatchDryRunPolicy retry, RouteMatchDryRunPolicy circuitBreaker, IReadOnlyList<RouteMatchDryRunFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredAction);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectiveAction);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalTarget);
        ArgumentException.ThrowIfNullOrWhiteSpace(rewrittenTarget);
        if (wouldProxy && upstream is null)
        {
            throw new ArgumentException("A proxying route dry-run requires an upstream.", nameof(upstream));
        }

        return new MatchedRouteResult(evaluatedAtUtc, noMatchReason, listener, route, configuredAction, effectiveAction, wouldProxy, generatedStatusCode, originalTarget, rewrittenTarget, upstream, cache, retry, circuitBreaker, findings);
    }
}
