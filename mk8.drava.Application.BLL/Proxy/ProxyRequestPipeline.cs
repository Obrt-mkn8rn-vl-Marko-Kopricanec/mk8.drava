using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.ControlPlane.Upgrades;
using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
using System.Runtime.CompilerServices;

namespace Mk8.Drava.Application.BLL.Proxy;

// The same policy owner serves every public client protocol. Socket/framing work stays in the executor.
public sealed class ProxyRequestPipeline(ProxyPipelineServices services)
{
    private readonly ConditionalWeakTable<RuntimeRoute, UpstreamSelectionRoute> _selectionRoutes = new();
    public async ValueTask ExecuteAsync(ProxyRequest request, IProxyExchangeExecutor executor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executor);
        var snapshot = services.Configuration.Snapshot;
        var listener = FindListener(snapshot, request.ListenerId);
        if (listener is null) throw new InvalidDataException("Unrecognized presentation listener.");
        var context = new ProxyRequestContext(services.RequestIds.Create(), listener.Name,
            ProxyRequestContextRuntimeMapper.ToTransport(listener), request.Peer.Endpoint, snapshot.Version, services.Clock, request.ClientProtocol);
        context.SetRequest(request.Head.Method, request.Head.Host, request.Head.Target, ProxyExternalRequestIdPolicy.Extract(request.Head));
        services.Metrics.RequestReceived();
        try
        {
            await ProcessAsync(request, snapshot, listener, context, executor, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            services.Observer.Complete(context, snapshot);
        }
    }

    private static RuntimeListener? FindListener(ProxyConfigurationSnapshot snapshot, string name)
    {
        RuntimeListener? found = null;
        foreach (var listener in snapshot.Listeners)
        {
            if (!string.Equals(listener.Name, name, StringComparison.Ordinal)) continue;
            if (found is not null) throw new InvalidDataException("Presentation listener identity is ambiguous.");
            found = listener;
        }
        return found;
    }

    private async ValueTask ProcessAsync(ProxyRequest request, ProxyConfigurationSnapshot snapshot, RuntimeListener listener,
        ProxyRequestContext context, IProxyExchangeExecutor executor, CancellationToken cancellationToken)
    {
        var forwarded = services.ForwardedHeaders.Build(request.Head, ProxyForwardedHeadersRuntimeMapper.ToListener(listener), snapshot.ForwardedHeaders, request.Peer);
        context.SetClientEndpoint(forwarded.ResolvedClientEndpoint);
        if (services.RateLimiter.AcquireRequest(forwarded.ResolvedClientAddress, snapshot.Limits.RequestsPerMinutePerIp) is ClientRateLimitDecision.RejectedResult)
        {
            await FailureAsync(ProxyFailureKind.RateLimited, context, executor, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (ProxyRequestMethodPolicy.IsConnectTunnelMethod(request.Head.Method))
        {
            await FailureAsync(ProxyFailureKind.ClientMalformedRequest, context, executor, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (services.Challenges.CreateResponse(request.Head) is AcmeHttp01ChallengeResponseResult.HandledResult challenge)
        {
            await GeneratedAsync(challenge.Response, context, executor, cancellationToken).ConfigureAwait(false);
            return;
        }
        var match = services.RouteMatcher.Match(ProxyRouteMatchRuntimeMapper.ToCandidates(snapshot.Routes), ProxyRouteMatchRuntimeMapper.ToRequest(request.Head));
        if (match is null)
        {
            await FailureAsync(ProxyFailureKind.NoMatchingRoute, context, executor, cancellationToken).ConfigureAwait(false);
            return;
        }
        var route = ProxyRouteMatchRuntimeMapper.SelectRoute(snapshot.Routes, match);
        context.SetRoute(ProxyRequestContextRuntimeMapper.ToRequestRoute(route));
        await ProcessRouteAsync(request, snapshot, listener, route, forwarded, context, executor, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ProcessRouteAsync(ProxyRequest request, ProxyConfigurationSnapshot snapshot, RuntimeListener listener,
        RuntimeRoute route, ForwardedHeadersContext forwarded, ProxyRequestContext context, IProxyExchangeExecutor executor, CancellationToken cancellationToken)
    {
        var isUpgrade = services.Upgrades.IsUpgradeRequest(request.Head);
        var action = services.RouteActions.Evaluate(ProxyRouteActionRuntimeMapper.ToPolicyInput(route, request.Head, listener, isUpgrade));
        if (!action.ShouldProxy)
        {
            await GeneratedAsync(action.Response!, context, executor, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (request.DeclaredBodyBytes > route.ResolvedOptions.MaxRequestBodyBytes)
        {
            services.Metrics.RequestBodySizeRejected();
            await FailureAsync(ProxyFailureKind.RequestPayloadTooLarge, context, executor, cancellationToken).ConfigureAwait(false);
            return;
        }
        var target = services.PathRewrite.Apply(ProxyPathRewriteRuntimeMapper.ToPolicyInput(route), request.Head.Target, request.Head.Path);
        var timeouts = ProxyTimeoutPolicy.ApplyRouteTimeouts(ProxyTimeoutRuntimeMapper.ToPolicyInput(route), snapshot.Timeouts);
        if (isUpgrade)
        {
            await UpgradeAsync(request, snapshot, listener, route, forwarded, target, timeouts, context, executor, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (services.Cache.Get(ProxyCacheRuntimeMapper.ToRequestScope(route, listener), request.Head, target) is ProxyCacheLookupResult.HitResult cached)
        {
            await executor.CachedAsync(cached.Response, context.RequestId, cancellationToken).ConfigureAwait(false);
            context.RecordCachedResponse(cached.Response, keepClientConnectionOpen: false);
            return;
        }
        if (route.Upstreams.Count == 0)
        {
            await FailureAsync(ProxyFailureKind.NoHealthyUpstream, context, executor, cancellationToken).ConfigureAwait(false);
            return;
        }
        var forwarding = new ProxyForwardingContext(request.Head, route, route.Upstreams[0], listener, timeouts,
            snapshot.ConnectionLimits, snapshot.Limits, target, forwarded, context.RequestId, false);
        await ForwardAsync(forwarding, context, executor, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ForwardAsync(ProxyForwardingContext forwarding, ProxyRequestContext context,
        IProxyExchangeExecutor executor, CancellationToken cancellationToken)
    {
        var plan = ProxyRetryPolicy.CreatePlan(ProxyRetryRuntimeMapper.ToAdmissionInput(forwarding.Route, forwarding.Head));
        // A bodyless write is still a write. It requires an explicit future idempotency contract; config alone cannot replay it.
        var retryAllowed = plan.IsAllowed && forwarding.Head.Method is "GET" or "HEAD" or "OPTIONS";
        var attempts = retryAllowed ? plan.MaxAttempts : 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var reservation = Reserve(forwarding.Route, forwarding.Head);
            if (reservation is null)
            {
                await FailureAsync(ProxyFailureKind.NoHealthyUpstream, context, executor, cancellationToken).ConfigureAwait(false);
                return;
            }
            var selection = reservation.Selection;
            context.SetUpstream(ProxyRequestContextRuntimeMapper.ToRequestUpstream(selection.Upstream));
            var suppress = attempt < attempts;
            var result = await executor.ForwardAsync(forwarding with { Upstream = selection.Upstream, SuppressFailureResponse = suppress }, cancellationToken).ConfigureAwait(false);
            ProxyUpstreamAttemptRecorder.Record(selection, result, services.Health, services.Circuits);
            reservation.Dispose();
            if (result is ForwardingResult.FailureResult { ResponseStarted: true })
                throw new IOException("Upstream forwarding failed after the response started.");
            var decision = retryAllowed ? ProxyRetryPolicy.EvaluateAttempt(ProxyRetryRuntimeMapper.ToOutcomeInput(forwarding.Route.Retry), result, attempt, attempts) : ProxyRetryAttemptDecision.Stop;
            if (decision == ProxyRetryAttemptDecision.Retry)
            {
                services.Metrics.RetryAttempted();
                if (forwarding.Route.Retry.RetryBackoff > TimeSpan.Zero)
                    await Task.Delay(forwarding.Route.Retry.RetryBackoff, services.Clock, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (suppress && result is ForwardingResult.FailureResult { ResponseStarted: false } failure)
                await FailureAsync(failure.FailureKind, context, executor, cancellationToken).ConfigureAwait(false);
            else context.RecordForwardingResult(result, keepClientConnectionOpen: false);
            return;
        }
    }

    private async ValueTask UpgradeAsync(ProxyRequest request, ProxyConfigurationSnapshot snapshot, RuntimeListener listener,
        RuntimeRoute route, ForwardedHeadersContext forwarded, string target, RuntimeTimeouts timeouts,
        ProxyRequestContext context, IProxyExchangeExecutor executor, CancellationToken cancellationToken)
    {
        context.RecordUpgradeRequest();
        services.Metrics.UpgradeRequestReceived();
        if (services.RateLimiter.AcquireUpgrade(forwarded.ResolvedClientAddress, snapshot.Limits.UpgradeRequestsPerMinutePerIp) is ClientRateLimitDecision.RejectedResult)
        {
            await FailureAsync(ProxyFailureKind.UpgradeRateLimited, context, executor, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (services.Upgrades.Validate(request.Head) is not UpgradeRequestValidationDecision.AcceptedDecision accepted)
        {
            await FailureAsync(ProxyFailureKind.UpgradeValidationFailed, context, executor, cancellationToken).ConfigureAwait(false);
            return;
        }
        using var reservation = Reserve(route, request.Head);
        if (reservation is null)
        {
            await FailureAsync(ProxyFailureKind.NoHealthyUpstream, context, executor, cancellationToken).ConfigureAwait(false);
            return;
        }
        var selection = reservation.Selection;
        context.SetUpstream(ProxyRequestContextRuntimeMapper.ToRequestUpstream(selection.Upstream));
        var result = await executor.UpgradeAsync(new ProxyForwardingContext(request.Head, route, selection.Upstream, listener,
            timeouts, snapshot.ConnectionLimits, snapshot.Limits, target, forwarded, context.RequestId, false), accepted.Upgrade, cancellationToken).ConfigureAwait(false);
        ProxyUpstreamAttemptRecorder.Record(selection, result, services.Health, services.Circuits);
        context.RecordForwardingResult(result, keepClientConnectionOpen: false);
        if (result is ForwardingResult.TunnelCompletedResult tunnel) context.RecordTunnelCompletion(tunnel);
    }

    private UpstreamReservation? Reserve(RuntimeRoute route, Http1RequestHead head)
    {
        var selection = _selectionRoutes.GetValue(route, ProxyUpstreamSelectionRuntimeMapper.ToSelectionRoute);
        string? key = null;
        if (selection.Policy.AffinityHeader is { } header)
            foreach (var field in head.Headers)
                if (string.Equals(field.Name, header, StringComparison.OrdinalIgnoreCase))
                {
                    if (key is not null) throw new InvalidDataException("Affinity header must occur once.");
                    key = field.Value;
                }
        return services.Selector.Reserve(selection, key);
    }

    private async ValueTask FailureAsync(ProxyFailureKind kind, ProxyRequestContext context, IProxyExchangeExecutor executor, CancellationToken cancellationToken)
    {
        var status = kind switch
        {
            ProxyFailureKind.NoMatchingRoute => 404,
            ProxyFailureKind.RateLimited or ProxyFailureKind.UpgradeRateLimited => 429,
            ProxyFailureKind.UpgradeValidationFailed => 400,
            _ => ProxyForwardingFailurePolicy.StatusCodeForFailure(kind),
        };
        var response = ProxyGeneratedFailurePolicy.BuildFailureResponse(status, ProxyRouteActionPolicy.ReasonPhrase(status), kind);
        services.Metrics.GeneratedFailureResponse(response.StatusCode);
        await GeneratedAsync(new GeneratedRouteResponse(response.StatusCode, response.ReasonPhrase, ProxyGeneratedFailurePolicy.PlainTextContentType, response.Body, []), context, executor, cancellationToken).ConfigureAwait(false);
        context.RecordGeneratedFailureResponse(response, keepClientConnectionOpen: false);
    }

    private static async ValueTask GeneratedAsync(GeneratedRouteResponse response, ProxyRequestContext context, IProxyExchangeExecutor executor, CancellationToken cancellationToken)
    {
        await executor.GeneratedAsync(response, context.RequestId, cancellationToken).ConfigureAwait(false);
        context.RecordGeneratedRouteResponse(response, keepClientConnectionOpen: false);
    }
}
