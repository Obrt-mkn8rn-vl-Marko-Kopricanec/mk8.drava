using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
using Mk8.Drava.Application.BLL.ControlPlane.Upgrades;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Net.Sockets;
using System.Text;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.Application.INF.Proxy.Http1;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Http2;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Http3;
using Mk8.Drava.Application.DAL.Observability;
using Mk8.Drava.Application.INF.Observability;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Tls;
using System.Net;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Connections;
internal sealed partial class ClientConnection
{
    private readonly Socket _socket;
    private readonly ProxyConfigurationSnapshot _configurationSnapshot;
    private readonly IReadOnlyList<RouteMatchCandidate> _routeCandidates;
    private readonly RuntimeListener _listener;
    private readonly IRouteMatcher _routeMatcher;
    private readonly IUpstreamSelector _upstreamSelector;
    private readonly UpstreamHealthStore _healthStore;
    private readonly ProxyForwarder _forwarder;
    private readonly UpgradeForwarder _upgradeForwarder;
    private readonly UpgradeRequestPolicy _upgradeRequestPolicy;
    private readonly ForwardedHeadersPolicy _forwardedHeadersPolicy;
    private readonly ProxyRouteActionPolicy _routeActionPolicy;
    private readonly PathRewritePolicy _pathRewritePolicy;
    private readonly ResponseCacheStore _cacheStore;
    private readonly Http3AltSvcPolicy _altSvcPolicy;
    private readonly CircuitBreakerStore _circuitBreakerStore;
    private readonly AcmeHttp01ChallengeResponder _acmeChallengeResponder;
    private readonly TlsConnectionAuthenticator _tlsAuthenticator;
    private readonly ProxyMetrics _metrics;
    private readonly RequestIdGenerator _requestIdGenerator;
    private readonly AccessLogEmitter _accessLogEmitter;
    private readonly ClientRateLimiter _rateLimiter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClientConnection> _logger;
    public ClientConnection(Socket socket, ProxyConfigurationSnapshot configurationSnapshot, RuntimeListener listener, IRouteMatcher routeMatcher, IUpstreamSelector upstreamSelector, UpstreamHealthStore healthStore, ProxyForwarder forwarder, UpgradeForwarder upgradeForwarder, UpgradeRequestPolicy upgradeRequestPolicy, ForwardedHeadersPolicy forwardedHeadersPolicy, ProxyRouteActionPolicy routeActionPolicy, PathRewritePolicy pathRewritePolicy, ResponseCacheStore cacheStore, Http3AltSvcPolicy altSvcPolicy, CircuitBreakerStore circuitBreakerStore, AcmeHttp01ChallengeResponder acmeChallengeResponder, TlsConnectionAuthenticator tlsAuthenticator, ProxyMetrics metrics, RequestIdGenerator requestIdGenerator, AccessLogEmitter accessLogEmitter, ClientRateLimiter rateLimiter, TimeProvider timeProvider, ILogger<ClientConnection> logger)
    {
        ArgumentNullException.ThrowIfNull(configurationSnapshot);
        _socket = socket;
        _configurationSnapshot = configurationSnapshot;
        _routeCandidates = ProxyRouteMatchRuntimeMapper.ToCandidates(configurationSnapshot.Routes);
        _listener = listener;
        _routeMatcher = routeMatcher;
        _upstreamSelector = upstreamSelector;
        _healthStore = healthStore;
        _forwarder = forwarder;
        _upgradeForwarder = upgradeForwarder;
        _upgradeRequestPolicy = upgradeRequestPolicy;
        _forwardedHeadersPolicy = forwardedHeadersPolicy;
        _routeActionPolicy = routeActionPolicy;
        _pathRewritePolicy = pathRewritePolicy;
        _cacheStore = cacheStore;
        _altSvcPolicy = altSvcPolicy;
        _circuitBreakerStore = circuitBreakerStore;
        _acmeChallengeResponder = acmeChallengeResponder;
        _tlsAuthenticator = tlsAuthenticator;
        _metrics = metrics;
        _requestIdGenerator = requestIdGenerator;
        _accessLogEmitter = accessLogEmitter;
        _rateLimiter = rateLimiter;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask RunAsync(CancellationToken cancellationToken)
    {
        _socket.NoDelay = true;
        var transportStream = new NetworkStream(_socket, ownsSocket: true);
        await using var ownedTransportStream = transportStream.ConfigureAwait(false);
        Stream clientStream = transportStream;
        TlsAuthenticationResult? tlsResult = null;
        if (_listener.Transport == RuntimeListenerTransport.Https)
        {
            tlsResult = await _tlsAuthenticator.AuthenticateAsync(transportStream, _configurationSnapshot, _listener, cancellationToken).ConfigureAwait(false);
            if (tlsResult is null)
            {
                return;
            }

            clientStream = tlsResult.Stream;
        }

        await using var ownedClientStream = clientStream.ConfigureAwait(false);
        if (tlsResult?.NegotiatedHttp2 == true)
        {
            using var http2Connection = new Http2ClientConnection(clientStream, GetRemoteEndPoint(), _configurationSnapshot, _listener, _routeMatcher, _upstreamSelector, _healthStore, _forwarder, _forwardedHeadersPolicy, _routeActionPolicy, _pathRewritePolicy, _cacheStore, _altSvcPolicy, _circuitBreakerStore, _acmeChallengeResponder, _metrics, _requestIdGenerator, _accessLogEmitter, _rateLimiter, _timeProvider, _logger);
            await http2Connection.RunAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_listener.Protocols.HasFlag(RuntimeListenerProtocols.Http1))
        {
            return;
        }

        var maxRequestHeadBytes = Math.Min(_listener.MaxRequestHeadBytes, _configurationSnapshot.Limits.MaxRequestHeadBytes);
        var requestHeadBuffer = ArrayPool<byte>.Shared.Rent(maxRequestHeadBytes);
        var requestsProcessed = 0;
        ProxyRequestContext? currentContext = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                currentContext = requestsProcessed == 0 ? CreateRequestContext() : null;
                var timeoutKind = requestsProcessed == 0 ? ProxyTimeoutKind.ClientRequestHead : ProxyTimeoutKind.ClientKeepAliveIdle;
                var timeout = requestsProcessed == 0 ? _configurationSnapshot.Timeouts.ClientRequestHeadTimeout : _configurationSnapshot.Timeouts.ClientKeepAliveIdleTimeout;
                var requestHeadRead = await Http1ClientRequestHeadReader.ReadAsync(clientStream, requestHeadBuffer, maxRequestHeadBytes, timeout, timeoutKind, _metrics, cancellationToken).ConfigureAwait(false);
                if (requestHeadRead.IsEmptyRequest)
                {
                    return;
                }

                currentContext ??= CreateRequestContext();
                if (requestHeadRead.IsRequestHeadTooLarge)
                {
                    _metrics.ParseFailed();
                    _metrics.ParserLimitRejected();
                    _metrics.MalformedRequestRejected();
                    await WriteGeneratedResponseAsync(clientStream, 431, "Request Header Fields Too Large", "Request Head Too Large", currentContext, ProxyFailureKind.ParserLimitExceeded, cancellationToken).ConfigureAwait(false);
                    CompleteContext(ref currentContext);
                    return;
                }

                if (requestHeadRead.IsIncompleteRequest)
                {
                    _metrics.ParseFailed();
                    _metrics.MalformedRequestRejected();
                    await WriteGeneratedResponseAsync(clientStream, 400, "Bad Request", "Bad Request", currentContext, ProxyFailureKind.ClientMalformedRequest, cancellationToken).ConfigureAwait(false);
                    CompleteContext(ref currentContext);
                    return;
                }

                var requestHeadBytes = requestHeadRead.HeadBytes;
                if (!Http1RequestParser.TryParse(requestHeadBytes.Span, new Http1RequestParseLimits(_configurationSnapshot.Limits.MaxHeaderCount, _configurationSnapshot.Limits.MaxHeaderLineBytes, _configurationSnapshot.Limits.MaxPathBytes), out var requestHead, out var parseError))
                {
                    _metrics.ParseFailed();
                    if (parseError is Http1ParseError.HeaderCountExceeded or Http1ParseError.HeaderLineTooLarge or Http1ParseError.TargetTooLarge)
                    {
                        _metrics.ParserLimitRejected();
                        await WriteGeneratedResponseAsync(clientStream, 431, "Request Header Fields Too Large", "Request Head Too Large", currentContext, ProxyFailureKind.ParserLimitExceeded, cancellationToken).ConfigureAwait(false);
                        CompleteContext(ref currentContext);
                        return;
                    }

                    if (parseError == Http1ParseError.UnsupportedTransferEncoding)
                    {
                        _metrics.UnsupportedRequestFramingRejected();
                        await WriteGeneratedResponseAsync(clientStream, 501, "Not Implemented", "Not Implemented", currentContext, ProxyFailureKind.ClientMalformedRequest, cancellationToken).ConfigureAwait(false);
                        CompleteContext(ref currentContext);
                        return;
                    }

                    _metrics.MalformedRequestRejected();
                    if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
                    {
                        LogRejectedMalformedRequestHeadWith10035(_logger, parseError, null);
                    }
                    await WriteGeneratedResponseAsync(clientStream, 400, "Bad Request", "Bad Request", currentContext, ProxyFailureKind.ClientMalformedRequest, cancellationToken).ConfigureAwait(false);
                    CompleteContext(ref currentContext);
                    return;
                }

                _metrics.RequestReceived();
                currentContext.SetRequest(requestHead.Method, requestHead.Host, requestHead.Target, ProxyExternalRequestIdPolicy.Extract(requestHead));
                var forwardedHeaders = _forwardedHeadersPolicy.Build(requestHead, ProxyForwardedHeadersRuntimeMapper.ToListener(_listener), _configurationSnapshot.ForwardedHeaders, ProxyClientAddressPolicy.ToForwardedHeadersPeer(GetRemoteEndPoint()));
                currentContext.SetClientEndpoint(forwardedHeaders.ResolvedClientEndpoint);
                if (_rateLimiter.AcquireRequest(forwardedHeaders.ResolvedClientAddress, _configurationSnapshot.Limits.RequestsPerMinutePerIp) is ClientRateLimitDecision.RejectedResult)
                {
                    await WriteGeneratedResponseAsync(clientStream, 429, "Too Many Requests", "Too Many Requests", currentContext, ProxyFailureKind.RateLimited, cancellationToken).ConfigureAwait(false);
                    CompleteContext(ref currentContext);
                    return;
                }

                if (ProxyRequestMethodPolicy.IsConnectTunnelMethod(requestHead.Method))
                {
                    _metrics.UnsupportedRequestFramingRejected();
                    await WriteGeneratedResponseAsync(clientStream, 501, "Not Implemented", "Not Implemented", currentContext, ProxyFailureKind.ClientMalformedRequest, cancellationToken).ConfigureAwait(false);
                    CompleteContext(ref currentContext);
                    return;
                }

                if (_acmeChallengeResponder.CreateResponse(requestHead)is AcmeHttp01ChallengeResponseResult.HandledResult acmeChallengeResponse)
                {
                    await WriteGeneratedRouteResponseAsync(clientStream, acmeChallengeResponse.Response, currentContext, cancellationToken).ConfigureAwait(false);
                    CompleteContext(ref currentContext);
                    return;
                }

                if (_upgradeRequestPolicy.IsUpgradeRequest(requestHead))
                {
                    var shouldContinue = await HandleUpgradeAsync(clientStream, requestHead, forwardedHeaders, currentContext, cancellationToken).ConfigureAwait(false);
                    CompleteContext(ref currentContext);
                    if (!shouldContinue)
                    {
                        return;
                    }

                    continue;
                }

                var routeMatch = _routeMatcher.Match(_routeCandidates, ProxyRouteMatchRuntimeMapper.ToRequest(requestHead));
                if (routeMatch is null)
                {
                    await WriteGeneratedResponseAsync(clientStream, 404, "Not Found", "Not Found", currentContext, ProxyFailureKind.NoMatchingRoute, cancellationToken).ConfigureAwait(false);
                    CompleteContext(ref currentContext);
                    return;
                }

                var route = ProxyRouteMatchRuntimeMapper.SelectRoute(_configurationSnapshot.Routes, routeMatch);
                currentContext.SetRoute(ProxyRequestContextRuntimeMapper.ToRequestRoute(route));
                if (await TryHandleGeneratedRouteActionAsync(clientStream, route, requestHead, currentContext, cancellationToken).ConfigureAwait(false))
                {
                    CompleteContext(ref currentContext);
                    return;
                }

                if (await TryRejectKnownLengthRequestBodyAsync(clientStream, route, requestHead, currentContext, cancellationToken).ConfigureAwait(false))
                {
                    CompleteContext(ref currentContext);
                    return;
                }

                var nextRequestCount = requestsProcessed + 1;
                var preferKeepAlive = Http1ClientConnectionPolicy.ShouldKeepOpen(requestHead) && nextRequestCount < _configurationSnapshot.ConnectionLimits.MaxRequestsPerClientConnection;
                var upstreamTarget = _pathRewritePolicy.Apply(ProxyPathRewriteRuntimeMapper.ToPolicyInput(route), requestHead.Target, requestHead.Path);
                var effectiveTimeouts = ProxyTimeoutPolicy.ApplyRouteTimeouts(ProxyTimeoutRuntimeMapper.ToPolicyInput(route), _configurationSnapshot.Timeouts);
                if (await TryHandleCacheHitAsync(clientStream, route, requestHead, upstreamTarget, preferKeepAlive, currentContext, effectiveTimeouts, cancellationToken).ConfigureAwait(false))
                {
                    requestsProcessed = nextRequestCount;
                    if (requestsProcessed >= _configurationSnapshot.ConnectionLimits.MaxRequestsPerClientConnection)
                    {
                        _metrics.ClientConnectionClosedByMaxRequests();
                        currentContext.RecordClientConnectionClose();
                        CompleteContext(ref currentContext);
                        return;
                    }

                    CompleteContext(ref currentContext);
                    if (!preferKeepAlive)
                    {
                        return;
                    }

                    continue;
                }

                var result = await ForwardWithRetriesAsync(clientStream, requestHeadRead, requestHead, route, _listener, effectiveTimeouts, _configurationSnapshot.ConnectionLimits, _configurationSnapshot.Limits, upstreamTarget, forwardedHeaders, preferKeepAlive, currentContext, currentContext.RequestId, cancellationToken).ConfigureAwait(false);
                requestsProcessed = nextRequestCount;
                ApplyForwardingResult(currentContext, result);
                if (requestsProcessed >= _configurationSnapshot.ConnectionLimits.MaxRequestsPerClientConnection)
                {
                    _metrics.ClientConnectionClosedByMaxRequests();
                    currentContext.RecordClientConnectionClose();
                    CompleteContext(ref currentContext);
                    return;
                }

                CompleteContext(ref currentContext);
                if (result is ForwardingResult.FailureResult || !result.KeepClientConnectionOpen)
                {
                    return;
                }
            }
        }
        catch (ProxyTimeoutException exception)when (exception.Kind == ProxyTimeoutKind.ClientRequestHead)
        {
            _metrics.ClientRequestHeadTimedOut();
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogClientTimedOutBeforeSending10036(_logger, exception);
            }
            if (currentContext is not null)
            {
                await WriteGeneratedResponseAsync(clientStream, 408, "Request Timeout", "Request Timeout", currentContext, ProxyFailureKind.ClientRequestHeadTimeout, cancellationToken).ConfigureAwait(false);
                CompleteContext(ref currentContext);
            }
        }
        catch (ProxyTimeoutException exception)when (exception.Kind == ProxyTimeoutKind.ClientKeepAliveIdle)
        {
            _metrics.ClientConnectionClosedByIdleTimeout();
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogClientKeepAliveIdleTimeout10037(_logger, exception);
            }
        }
        catch (ProxyTimeoutException exception)when (exception.Kind == ProxyTimeoutKind.DownstreamWrite)
        {
            _metrics.DownstreamWriteTimedOut();
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogTimedOutWhileWritingA10038(_logger, exception);
            }
            if (currentContext is not null)
            {
                currentContext.RecordClientDisconnect();
                CompleteContext(ref currentContext);
            }
        }
        catch (IOException exception)when (IsClientDisconnect(exception))
        {
            _metrics.ClientPrematureDisconnect();
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogClientDisconnectedDuringRequestProcessing10039(_logger, exception);
            }
            if (currentContext is not null)
            {
                currentContext.RecordClientDisconnect();
                CompleteContext(ref currentContext);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(requestHeadBuffer);
        }
    }

    private async ValueTask<bool> TryHandleGeneratedRouteActionAsync(Stream clientStream, RuntimeRoute route, Http1RequestHead requestHead, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        var actionDecision = _routeActionPolicy.Evaluate(ProxyRouteActionRuntimeMapper.ToPolicyInput(route, requestHead, _listener, isUpgradeRequest: false));
        if (actionDecision.ShouldProxy)
        {
            return false;
        }

        await WriteGeneratedRouteResponseAsync(clientStream, actionDecision.Response!, context, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> TryRejectKnownLengthRequestBodyAsync(Stream clientStream, RuntimeRoute route, Http1RequestHead requestHead, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        if (requestHead.Framing.Kind != Http1BodyKind.ContentLength || requestHead.Framing.ContentLength.GetValueOrDefault() <= route.ResolvedOptions.MaxRequestBodyBytes)
        {
            return false;
        }

        _metrics.RequestBodySizeRejected();
        await WriteGeneratedResponseAsync(clientStream, 413, "Payload Too Large", "Payload Too Large", context, ProxyFailureKind.RequestPayloadTooLarge, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> TryHandleCacheHitAsync(Stream clientStream, RuntimeRoute route, Http1RequestHead requestHead, string upstreamTarget, bool keepClientConnectionOpen, ProxyRequestContext context, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var cacheLookup = _cacheStore.Get(ProxyCacheRuntimeMapper.ToRequestScope(route, _listener), requestHead, upstreamTarget);
        if (cacheLookup is not ProxyCacheLookupResult.HitResult cacheHit)
        {
            return false;
        }

        await WriteCachedResponseAsync(clientStream, requestHead, cacheHit.Response, keepClientConnectionOpen, context, timeouts, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<ForwardingResult> ForwardWithRetriesAsync(Stream clientStream, Http1HeadReadResult requestHeadRead, Http1RequestHead requestHead, RuntimeRoute route, RuntimeListener listener, RuntimeTimeouts timeouts, RuntimeConnectionLimits connectionLimits, RuntimeLimits limits, string upstreamTarget, ForwardedHeadersContext forwardedHeaders, bool preferClientKeepAlive, ProxyRequestContext context, string requestId, CancellationToken cancellationToken)
    {
        var retryPlan = ProxyRetryPolicy.CreatePlan(ProxyRetryRuntimeMapper.ToAdmissionInput(route, requestHead));
        if (retryPlan.Admission is ProxyRetryAdmissionDecision.SkippedDecision skippedAdmission)
        {
            _metrics.RetrySkipped(skippedAdmission.Reason);
        }

        var retryAllowed = retryPlan.IsAllowed;
        var maxAttempts = retryPlan.MaxAttempts;
        var retryOutcome = ProxyRetryRuntimeMapper.ToOutcomeInput(route.Retry);
        ForwardingResult? lastResult = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var selection = _upstreamSelector.Select(ProxyUpstreamSelectionRuntimeMapper.ToSelectionRoute(route));
            if (selection is null)
            {
                if (ProxyRetryPolicy.DidExhaustAttemptsBeforeUpstreamSelection(attempt))
                {
                    _metrics.RetryExhausted();
                }

                var failureResponse = ProxyGeneratedFailurePolicy.BuildFailureResponse(ProxyFailureKind.NoHealthyUpstream);
                ProxyGeneratedFailureMetrics.Record(_metrics, failureResponse);
                await WriteGeneratedResponseAsync(clientStream, failureResponse, context, cancellationToken).ConfigureAwait(false);
                return failureResponse.ToForwardingResult();
            }

            context.SetUpstream(ProxyRequestContextRuntimeMapper.ToRequestUpstream(selection.Upstream));
            var suppressGeneratedFailureResponse = ProxyRetryPolicy.ShouldSuppressAttemptFailureResponse(retryAllowed, attempt, maxAttempts);
            var result = await _forwarder.ForwardAsync(clientStream, requestHeadRead, requestHead, route, selection.Upstream, listener, ProxyTimeoutPolicy.ApplyRetryAttemptTimeout(ProxyTimeoutRuntimeMapper.ToPolicyInput(route), timeouts), connectionLimits, limits, upstreamTarget, forwardedHeaders, preferClientKeepAlive, requestId, cancellationToken, suppressGeneratedFailureResponse).ConfigureAwait(false);
            lastResult = result;
            ProxyUpstreamAttemptRecorder.Record(selection, result, _healthStore, _circuitBreakerStore);
            var retryAttempt = retryAllowed ? ProxyRetryPolicy.EvaluateAttempt(retryOutcome, result, attempt, maxAttempts) : ProxyRetryAttemptDecision.Stop;
            if (retryAttempt == ProxyRetryAttemptDecision.Retry)
            {
                _metrics.RetryAttempted();
                if (route.Retry.RetryBackoff > TimeSpan.Zero)
                {
                    await Task.Delay(route.Retry.RetryBackoff, _timeProvider, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            if (retryAttempt is ProxyRetryAttemptDecision.SkippedDecision skippedAttempt)
            {
                _metrics.RetrySkipped(skippedAttempt.Reason);
            }

            if (retryAllowed && ProxyRetryPolicy.DidExhaustAttempts(retryOutcome, result, attempt, maxAttempts))
            {
                _metrics.RetryExhausted();
            }

            if (suppressGeneratedFailureResponse && result is ForwardingResult.FailureResult { ResponseStarted: false } suppressedFailure)
            {
                return await WriteSuppressedFailureAsync(clientStream, suppressedFailure, context, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }

        if (lastResult is ForwardingResult.FailureResult { ResponseStarted: false } lastFailure)
        {
            return await WriteSuppressedFailureAsync(clientStream, lastFailure, context, cancellationToken).ConfigureAwait(false);
        }

        return ProxyRetryPolicy.RequireCompletedAttemptResult(lastResult);
    }

    private async ValueTask<bool> HandleUpgradeAsync(Stream clientStream, Http1RequestHead requestHead, ForwardedHeadersContext forwardedHeaders, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        context.RecordUpgradeRequest();
        _metrics.UpgradeRequestReceived();
        if (_rateLimiter.AcquireUpgrade(forwardedHeaders.ResolvedClientAddress, _configurationSnapshot.Limits.UpgradeRequestsPerMinutePerIp) is ClientRateLimitDecision.RejectedResult)
        {
            _metrics.UpgradeRequestRejected();
            await WriteGeneratedResponseAsync(clientStream, 429, "Too Many Requests", "Too Many Requests", context, ProxyFailureKind.UpgradeRateLimited, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var validation = _upgradeRequestPolicy.Validate(requestHead);
        if (validation is UpgradeRequestValidationDecision.RejectedDecision rejectedUpgrade)
        {
            _metrics.UpgradeRequestRejected();
            _metrics.MalformedRequestRejected();
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogRejectedUpgradeRequestFor10040(_logger, requestHead.Method, requestHead.Target, rejectedUpgrade.Reason, null);
            }
            await WriteGeneratedResponseAsync(clientStream, 400, "Bad Request", "Bad Request", context, ProxyFailureKind.UpgradeValidationFailed, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var upgrade = ((UpgradeRequestValidationDecision.AcceptedDecision)validation).Upgrade;
        var upgradeRouteMatch = _routeMatcher.Match(_routeCandidates, ProxyRouteMatchRuntimeMapper.ToRequest(requestHead));
        if (upgradeRouteMatch is null)
        {
            _metrics.UpgradeRequestRejected();
            await WriteGeneratedResponseAsync(clientStream, 404, "Not Found", "Not Found", context, ProxyFailureKind.NoMatchingRoute, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var upgradeRoute = ProxyRouteMatchRuntimeMapper.SelectRoute(_configurationSnapshot.Routes, upgradeRouteMatch);
        context.SetRoute(ProxyRequestContextRuntimeMapper.ToRequestRoute(upgradeRoute));
        var actionDecision = _routeActionPolicy.Evaluate(ProxyRouteActionRuntimeMapper.ToPolicyInput(upgradeRoute, requestHead, _listener, isUpgradeRequest: true));
        if (!actionDecision.ShouldProxy)
        {
            await WriteGeneratedRouteResponseAsync(clientStream, actionDecision.Response!, context, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var upgradeSelection = _upstreamSelector.Select(ProxyUpstreamSelectionRuntimeMapper.ToSelectionRoute(upgradeRoute));
        if (upgradeSelection is null)
        {
            _metrics.UpgradeRequestRejected();
            var failureResponse = ProxyGeneratedFailurePolicy.BuildFailureResponse(ProxyFailureKind.NoHealthyUpstream);
            ProxyGeneratedFailureMetrics.Record(_metrics, failureResponse);
            await WriteGeneratedResponseAsync(clientStream, failureResponse, context, cancellationToken).ConfigureAwait(false);
            return false;
        }

        context.SetUpstream(ProxyRequestContextRuntimeMapper.ToRequestUpstream(upgradeSelection.Upstream));
        var upstreamTarget = _pathRewritePolicy.Apply(ProxyPathRewriteRuntimeMapper.ToPolicyInput(upgradeRoute), requestHead.Target, requestHead.Path);
        var effectiveTimeouts = ProxyTimeoutPolicy.ApplyRouteTimeouts(ProxyTimeoutRuntimeMapper.ToPolicyInput(upgradeRoute), _configurationSnapshot.Timeouts);
        var upgradeResult = await _upgradeForwarder.ForwardAsync(clientStream, requestHead, upgrade, upgradeRoute, upgradeSelection.Upstream, _listener, effectiveTimeouts, _configurationSnapshot.ConnectionLimits, upstreamTarget, forwardedHeaders, context.RequestId, cancellationToken).ConfigureAwait(false);
        ProxyUpstreamAttemptRecorder.Record(upgradeSelection, upgradeResult, _healthStore, _circuitBreakerStore);
        ApplyForwardingResult(context, upgradeResult);
        return false;
    }

    private async ValueTask<ForwardingResult> WriteSuppressedFailureAsync(Stream clientStream, ForwardingResult.FailureResult result, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        var response = ProxyGeneratedFailurePolicy.BuildFailureResponse(result);
        ProxyGeneratedFailureMetrics.Record(_metrics, response);
        await WriteGeneratedResponseAsync(clientStream, response, context, cancellationToken).ConfigureAwait(false);
        return response.ToForwardingResult();
    }

    private async ValueTask WriteGeneratedResponseAsync(Stream clientStream, int statusCode, string reasonPhrase, string body, ProxyRequestContext context, ProxyFailureKind failureKind, CancellationToken cancellationToken)
    {
        await WriteGeneratedResponseAsync(clientStream, ProxyGeneratedFailurePolicy.BuildFailureResponse(statusCode, reasonPhrase, body, failureKind), context, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteGeneratedResponseAsync(Stream clientStream, ProxyGeneratedFailureResponse response, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        await ProxyErrorResponses.WriteGeneratedAsync(clientStream, response.StatusCode, response.ReasonPhrase, response.Body, context.RequestId, _configurationSnapshot.Timeouts.DownstreamWriteTimeout, _metrics, cancellationToken, contentType: ProxyGeneratedFailurePolicy.PlainTextContentType, headers: ApplyAltSvc([])).ConfigureAwait(false);
        context.RecordGeneratedFailureResponse(response, keepClientConnectionOpen: false);
    }

    private async ValueTask WriteCachedResponseAsync(Stream clientStream, Http1RequestHead requestHead, CachedProxyResponse response, bool keepClientConnectionOpen, ProxyRequestContext context, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var includeBody = !string.Equals(requestHead.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
        var ageSeconds = ProxyCacheAgePolicy.CalculateAgeSeconds(response.StoredAtUtc, _timeProvider.GetUtcNow());
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(response.ReasonPhrase).Append("\r\n");
        foreach (var header in response.Headers)
        {
            builder.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        }

        foreach (var header in ApplyAltSvc([]))
        {
            builder.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        }

        builder.Append("Age: ").Append(ageSeconds).Append("\r\n");
        builder.Append("X-Request-Id: ").Append(context.RequestId).Append("\r\n");
        if (response.ContentLength is { } length)
            builder.Append("Content-Length: ").Append(length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
        builder.Append(keepClientConnectionOpen ? "Connection: keep-alive\r\n\r\n" : "Connection: close\r\n\r\n");
        var headBytes = Encoding.ASCII.GetBytes(builder.ToString());
        await ProxyTimedStreamWriter.WriteAsync(clientStream, headBytes, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
        _metrics.AddBytesWritten(headBytes.Length);
        if (includeBody && response.Body.Length > 0)
        {
            await ProxyTimedStreamWriter.WriteAsync(clientStream, response.Body, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
            _metrics.AddBytesWritten(response.Body.Length);
        }

        context.RecordCachedResponse(response, keepClientConnectionOpen);
    }

    private async ValueTask WriteGeneratedRouteResponseAsync(Stream clientStream, GeneratedRouteResponse response, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        await ProxyErrorResponses.WriteGeneratedAsync(clientStream, response.StatusCode, response.ReasonPhrase, response.Body, context.RequestId, _configurationSnapshot.Timeouts.DownstreamWriteTimeout, _metrics, cancellationToken, contentType: response.ContentType, headers: ApplyAltSvc(response.Headers)).ConfigureAwait(false);
        context.RecordGeneratedRouteResponse(response, keepClientConnectionOpen: false);
    }

    private IReadOnlyList<ProxyHeaderField> ApplyAltSvc(IReadOnlyList<ProxyHeaderField> headers)
    {
        return Http3AltSvcPolicy.ApplyHeader(headers, _altSvcPolicy.CreateHeader(ProxyHttp3AltSvcRuntimeMapper.ToListenerInput(_listener)));
    }

    private ProxyRequestContext CreateRequestContext()
    {
        return new ProxyRequestContext(_requestIdGenerator.Create(), _listener.Name, ProxyRequestContextRuntimeMapper.ToTransport(_listener), _socket.RemoteEndPoint?.ToString(), _configurationSnapshot.Version, _timeProvider);
    }

    private IPEndPoint? GetRemoteEndPoint()
    {
        return _socket.RemoteEndPoint as IPEndPoint;
    }

    private void CompleteContext(ref ProxyRequestContext? context)
    {
        if (context is null)
        {
            return;
        }

        _accessLogEmitter.Complete(context, context.AccessLogEnabled ?? _configurationSnapshot.Observability.AccessLogEnabled, _configurationSnapshot.Observability.RecentDiagnosticsCapacity);
        context = null;
    }

    private static void ApplyForwardingResult(ProxyRequestContext context, ForwardingResult result)
    {
        context.RecordForwardingResult(result, result.KeepClientConnectionOpen);
        if (result is ForwardingResult.TunnelCompletedResult tunnelCompleted)
        {
            context.RecordTunnelCompletion(tunnelCompleted);
        }
    }

    private static bool IsClientDisconnect(IOException exception)
    {
        return exception.InnerException is SocketException;
    }
}
