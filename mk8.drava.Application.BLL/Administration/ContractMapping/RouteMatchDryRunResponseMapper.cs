using BusinessRouteMatchDryRunListener = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunListener;
using BusinessRouteMatchDryRunResult = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunResult;
using BusinessRouteMatchDryRunRoute = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunRoute;
using BusinessRouteMatchDryRunUpstream = Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics.RouteMatchDryRunUpstream;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RouteMatchDryRunResponseMapper
{
    public static RouteMatchDryRunResponse FromResult(BusinessRouteMatchDryRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            BusinessRouteMatchDryRunResult.FailedResult failed => FromResult(failed, succeeded: false, failureReason: failed.FailureReason, noMatchReason: null, listener: null, route: null, configuredAction: null, effectiveAction: null, wouldProxy: false, generatedStatusCode: null, originalTarget: null, rewrittenTarget: null, upstream: null),
            BusinessRouteMatchDryRunResult.NoMatchingListenerResult noListener => FromResult(noListener, succeeded: true, failureReason: null, noMatchReason: noListener.NoMatchReason, listener: null, route: null, configuredAction: null, effectiveAction: null, wouldProxy: false, generatedStatusCode: null, originalTarget: noListener.OriginalTarget, rewrittenTarget: null, upstream: null),
            BusinessRouteMatchDryRunResult.NoMatchingRouteResult noRoute => FromResult(noRoute, succeeded: true, failureReason: null, noMatchReason: noRoute.NoMatchReason, listener: noRoute.Listener, route: null, configuredAction: null, effectiveAction: null, wouldProxy: false, generatedStatusCode: null, originalTarget: noRoute.OriginalTarget, rewrittenTarget: null, upstream: null),
            BusinessRouteMatchDryRunResult.MatchedRouteResult matched => FromResult(matched, succeeded: true, failureReason: null, noMatchReason: matched.NoMatchReason, listener: matched.Listener, route: matched.Route, configuredAction: matched.ConfiguredAction, effectiveAction: matched.EffectiveAction, wouldProxy: matched.WouldProxy, generatedStatusCode: matched.GeneratedStatusCode, originalTarget: matched.OriginalTarget, rewrittenTarget: matched.RewrittenTarget, upstream: matched.Upstream),
            _ => throw new InvalidOperationException($"Unknown route dry-run result '{result.GetType().Name}'.")};
    }

    private static RouteMatchDryRunResponse FromResult(BusinessRouteMatchDryRunResult result, bool succeeded, string? failureReason, string? noMatchReason, BusinessRouteMatchDryRunListener? listener, BusinessRouteMatchDryRunRoute? route, string? configuredAction, string? effectiveAction, bool wouldProxy, int? generatedStatusCode, string? originalTarget, string? rewrittenTarget, BusinessRouteMatchDryRunUpstream? upstream)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new RouteMatchDryRunResponse(succeeded: succeeded, evaluatedAtUtc: result.EvaluatedAtUtc, failureReason: failureReason, noMatchReason: noMatchReason, listener: listener is null ? null : RouteMatchDryRunListenerResponseMapper.FromListener(listener), route: route is null ? null : RouteMatchDryRunRouteResponseMapper.FromRoute(route), configuredAction: configuredAction, effectiveAction: effectiveAction, wouldProxy: wouldProxy, generatedStatusCode: generatedStatusCode, originalTarget: originalTarget, rewrittenTarget: rewrittenTarget, upstream: upstream is null ? null : RouteMatchDryRunUpstreamResponseMapper.FromUpstream(upstream), cache: RouteMatchDryRunPolicyResponseMapper.FromPolicy(result.Cache), retry: RouteMatchDryRunPolicyResponseMapper.FromPolicy(result.Retry), circuitBreaker: RouteMatchDryRunPolicyResponseMapper.FromPolicy(result.CircuitBreaker), findings: RouteMatchDryRunFindingResponseMapper.FromFindings(result.Findings));
    }
}
