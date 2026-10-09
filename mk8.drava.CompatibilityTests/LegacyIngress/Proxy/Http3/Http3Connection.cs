using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
#pragma warning disable CA1416
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Quic;
using System.Text;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.Application.INF.Proxy.Http1;
using Mk8.Drava.Application.DAL.Observability;
using Mk8.Drava.Application.INF.Observability;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Http3;
internal sealed partial class Http3Connection
{
    private const int MaxFramePayloadBytes = 1024 * 1024;
    private const int MaxProtocolErrorsPerConnection = 8;
    private readonly QuicConnection _connection;
    private readonly ProxyConfigurationSnapshot _configurationSnapshot;
    private readonly IReadOnlyList<RouteMatchCandidate> _routeCandidates;
    private readonly RuntimeListener _listener;
    private readonly IRouteMatcher _routeMatcher;
    private readonly IUpstreamSelector _upstreamSelector;
    private readonly UpstreamHealthStore _healthStore;
    private readonly ProxyForwarder _forwarder;
    private readonly ForwardedHeadersPolicy _forwardedHeadersPolicy;
    private readonly ProxyRouteActionPolicy _routeActionPolicy;
    private readonly PathRewritePolicy _pathRewritePolicy;
    private readonly ResponseCacheStore _cacheStore;
    private readonly CircuitBreakerStore _circuitBreakerStore;
    private readonly AcmeHttp01ChallengeResponder _acmeChallengeResponder;
    private readonly ProxyMetrics _metrics;
    private readonly RequestIdGenerator _requestIdGenerator;
    private readonly AccessLogEmitter _accessLogEmitter;
    private readonly ClientRateLimiter _rateLimiter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private int _protocolErrors;
    private QuicStream? _localControlStream;
    public Http3Connection(QuicConnection connection, ProxyConfigurationSnapshot configurationSnapshot, RuntimeListener listener, IRouteMatcher routeMatcher, IUpstreamSelector upstreamSelector, UpstreamHealthStore healthStore, ProxyForwarder forwarder, ForwardedHeadersPolicy forwardedHeadersPolicy, ProxyRouteActionPolicy routeActionPolicy, PathRewritePolicy pathRewritePolicy, ResponseCacheStore cacheStore, CircuitBreakerStore circuitBreakerStore, AcmeHttp01ChallengeResponder acmeChallengeResponder, ProxyMetrics metrics, RequestIdGenerator requestIdGenerator, AccessLogEmitter accessLogEmitter, ClientRateLimiter rateLimiter, TimeProvider timeProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configurationSnapshot);
        _connection = connection;
        _configurationSnapshot = configurationSnapshot;
        _routeCandidates = ProxyRouteMatchRuntimeMapper.ToCandidates(configurationSnapshot.Routes);
        _listener = listener;
        _routeMatcher = routeMatcher;
        _upstreamSelector = upstreamSelector;
        _healthStore = healthStore;
        _forwarder = forwarder;
        _forwardedHeadersPolicy = forwardedHeadersPolicy;
        _routeActionPolicy = routeActionPolicy;
        _pathRewritePolicy = pathRewritePolicy;
        _cacheStore = cacheStore;
        _circuitBreakerStore = circuitBreakerStore;
        _acmeChallengeResponder = acmeChallengeResponder;
        _metrics = metrics;
        _requestIdGenerator = requestIdGenerator;
        _accessLogEmitter = accessLogEmitter;
        _rateLimiter = rateLimiter;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask RunAsync(CancellationToken cancellationToken)
    {
        _metrics.Http3ConnectionAccepted();
        await using var ownedConnection = _connection.ConfigureAwait(false);
        await SendSettingsAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var stream = await _connection.AcceptInboundStreamAsync(cancellationToken).ConfigureAwait(false);
                if (stream.Type == QuicStreamType.Bidirectional)
                {
                    if (!await ProcessRequestStreamAsync(stream, cancellationToken).ConfigureAwait(false))
                    {
                        await _connection.CloseAsync(0x100, CancellationToken.None).ConfigureAwait(false);
                        return;
                    }
                }
                else
                {
                    DrainUnsupportedStreamInBackground(stream, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (QuicException exception)
        {
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogHTTPQUICConnectionEnded10055(_logger, exception);
            }
        }
        catch (IOException exception)
        {
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogHTTPConnectionEndedWithI10056(_logger, exception);
            }
        }
        finally
        {
            if (_localControlStream is not null)
            {
                await _localControlStream.DisposeAsync().ConfigureAwait(false);
            }

            _metrics.Http3ConnectionClosed();
        }
    }

    private async ValueTask<bool> ProcessRequestStreamAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        await using var ownedStream = stream.ConfigureAwait(false);
        var context = CreateRequestContext();
        _metrics.Http3StreamStarted();
        try
        {
            var headerRead = await ReadRequestHeadersAsync(stream, cancellationToken).ConfigureAwait(false);
            var rejectionReason = headerRead.Reason;
            if (!headerRead.Success)
            {
                var closeConnection = RecordProtocolError(rejectionReason);
                await WriteGeneratedResponseAsync(stream, 400, "Bad Request", context, ProxyFailureKind.ClientMalformedRequest, "GET", cancellationToken).ConfigureAwait(false);
                CompleteContext(ref context);
                return !closeConnection;
            }

            var translationResult = Http3RequestTranslator.BuildRequest(headerRead.Headers, ProxyHttp3RequestTranslationRuntimeMapper.ToListenerInput(_listener), bodyMayFollow: true);
            if (translationResult is not Http3RequestTranslationResult.AcceptedResult translation)
            {
                rejectionReason = ((Http3RequestTranslationResult.RejectedResult)translationResult).Reason;
                var closeConnection = RecordProtocolError(rejectionReason);
                await WriteGeneratedResponseAsync(stream, 400, "Bad Request", context, ProxyFailureKind.ClientMalformedRequest, "GET", cancellationToken).ConfigureAwait(false);
                CompleteContext(ref context);
                return !closeConnection;
            }

            var requestHead = translation.RequestHead;
            _metrics.RequestReceived();
            _metrics.Http3RequestReceived();
            context.SetRequest(requestHead.Method, requestHead.Host, requestHead.Target, ProxyExternalRequestIdPolicy.Extract(requestHead));
            var methodDecision = ProxyRequestMethodPolicy.ClassifyApplicationMethod(requestHead.Method);
            if (methodDecision is ProxyRequestApplicationMethodDecision.RejectedDecision rejectedMethod)
            {
                _metrics.Http3RequestRejected(rejectedMethod.Reason);
                await WriteGeneratedResponseAsync(stream, 501, "Not Implemented", context, ProxyFailureKind.ClientMalformedRequest, requestHead.Method, cancellationToken).ConfigureAwait(false);
                CompleteContext(ref context);
                return true;
            }

            var noBodyFrames = requestHead.Framing.Kind == Http1BodyKind.None ? await EnsureNoRequestBodyFramesAsync(stream, cancellationToken).ConfigureAwait(false) : Http3FrameValidationResult.Successful();
            if (!noBodyFrames.Success)
            {
                var closeConnection = RecordProtocolError(noBodyFrames.Reason);
                await WriteGeneratedResponseAsync(stream, 400, "Bad Request", context, ProxyFailureKind.ClientMalformedRequest, requestHead.Method, cancellationToken).ConfigureAwait(false);
                CompleteContext(ref context);
                return !closeConnection;
            }

            var forwardedHeaders = _forwardedHeadersPolicy.Build(requestHead, ProxyForwardedHeadersRuntimeMapper.ToListener(_listener), _configurationSnapshot.ForwardedHeaders, ProxyClientAddressPolicy.ToForwardedHeadersPeer(_connection.RemoteEndPoint));
            context.SetClientEndpoint(forwardedHeaders.ResolvedClientEndpoint);
            if (_rateLimiter.AcquireRequest(forwardedHeaders.ResolvedClientAddress, _configurationSnapshot.Limits.RequestsPerMinutePerIp) is ClientRateLimitDecision.RejectedResult)
            {
                await WriteGeneratedResponseAsync(stream, 429, "Too Many Requests", context, ProxyFailureKind.RateLimited, requestHead.Method, cancellationToken).ConfigureAwait(false);
                CompleteContext(ref context);
                return true;
            }

            if (_acmeChallengeResponder.CreateResponse(requestHead)is AcmeHttp01ChallengeResponseResult.HandledResult acmeChallengeResponse)
            {
                _metrics.Http3GeneratedResponse();
                await WriteGeneratedRouteResponseAsync(stream, acmeChallengeResponse.Response, context, requestHead.Method, cancellationToken).ConfigureAwait(false);
                CompleteContext(ref context);
                return true;
            }

            var routeMatch = _routeMatcher.Match(_routeCandidates, ProxyRouteMatchRuntimeMapper.ToRequest(requestHead));
            if (routeMatch is null)
            {
                await WriteGeneratedResponseAsync(stream, 404, "Not Found", context, ProxyFailureKind.NoMatchingRoute, requestHead.Method, cancellationToken).ConfigureAwait(false);
                CompleteContext(ref context);
                return true;
            }

            var route = ProxyRouteMatchRuntimeMapper.SelectRoute(_configurationSnapshot.Routes, routeMatch);
            context.SetRoute(ProxyRequestContextRuntimeMapper.ToRequestRoute(route));
            if (await TryHandleGeneratedRouteActionAsync(stream, route, requestHead, context, cancellationToken).ConfigureAwait(false))
            {
                CompleteContext(ref context);
                return true;
            }

            if (await TryRejectKnownLengthRequestBodyAsync(stream, route, requestHead, context, cancellationToken).ConfigureAwait(false))
            {
                CompleteContext(ref context);
                return true;
            }

            using var requestBody = new Http3RequestBodyReadStream(this, stream, requestHead.Framing, cancellationToken);
            var upstreamTarget = _pathRewritePolicy.Apply(ProxyPathRewriteRuntimeMapper.ToPolicyInput(route), requestHead.Target, requestHead.Path);
            var effectiveTimeouts = ProxyTimeoutPolicy.ApplyRouteTimeouts(ProxyTimeoutRuntimeMapper.ToPolicyInput(route), _configurationSnapshot.Timeouts);
            if (await TryHandleCacheHitAsync(stream, route, requestHead, upstreamTarget, context, cancellationToken).ConfigureAwait(false))
            {
                CompleteContext(ref context);
                return true;
            }

            _metrics.Http3ProxiedRequest();
            var result = await ForwardWithRetriesAsync(stream, requestBody, requestHead, route, effectiveTimeouts, _configurationSnapshot.ConnectionLimits, _configurationSnapshot.Limits, upstreamTarget, forwardedHeaders, context, context.RequestId, cancellationToken).ConfigureAwait(false);
            ApplyForwardingResult(context, result);
            CompleteContext(ref context);
            return true;
        }
        catch (Exception exception)when (exception is QuicException or IOException)
        {
            _metrics.Http3ProtocolError("io_error");
            if (exception is QuicException)
            {
                _metrics.Http3StreamReset();
            }

            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogHTTPStreamEndedWithI10057(_logger, exception);
            }
            CompleteContext(ref context);
            return true;
        }
        finally
        {
            _metrics.Http3StreamEnded();
        }
    }

    private async ValueTask<bool> TryHandleGeneratedRouteActionAsync(QuicStream stream, RuntimeRoute route, Http1RequestHead requestHead, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        var actionDecision = _routeActionPolicy.Evaluate(ProxyRouteActionRuntimeMapper.ToPolicyInput(route, requestHead, _listener, isUpgradeRequest: false));
        if (actionDecision.ShouldProxy)
        {
            return false;
        }

        _metrics.Http3GeneratedResponse();
        await WriteGeneratedRouteResponseAsync(stream, actionDecision.Response!, context, requestHead.Method, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> TryRejectKnownLengthRequestBodyAsync(QuicStream stream, RuntimeRoute route, Http1RequestHead requestHead, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        if (requestHead.Framing.Kind != Http1BodyKind.ContentLength || requestHead.Framing.ContentLength.GetValueOrDefault() <= route.ResolvedOptions.MaxRequestBodyBytes)
        {
            return false;
        }

        _metrics.RequestBodySizeRejected();
        _metrics.Http3RequestRejected("request_body_too_large");
        await WriteGeneratedResponseAsync(stream, 413, "Payload Too Large", context, ProxyFailureKind.RequestPayloadTooLarge, requestHead.Method, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> TryHandleCacheHitAsync(QuicStream stream, RuntimeRoute route, Http1RequestHead requestHead, string upstreamTarget, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        var cacheLookup = _cacheStore.Get(ProxyCacheRuntimeMapper.ToRequestScope(route, _listener), requestHead, upstreamTarget);
        if (cacheLookup is not ProxyCacheLookupResult.HitResult cacheHit)
        {
            return false;
        }

        await WriteCachedResponseAsync(stream, requestHead, cacheHit.Response, context, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask SendSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _localControlStream = await _connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cancellationToken).ConfigureAwait(false);
            using var payload = new MemoryStream();
            Http3Codec.WriteVarInt(payload, Http3Codec.ControlStream);
            using var settings = new MemoryStream();
            Http3Codec.WriteVarInt(settings, Http3Codec.QpackMaxTableCapacitySetting);
            Http3Codec.WriteVarInt(settings, 0);
            Http3Codec.WriteVarInt(settings, Http3Codec.QpackBlockedStreamsSetting);
            Http3Codec.WriteVarInt(settings, 0);
            Http3Codec.WriteFrame(payload, Http3Codec.SettingsFrame, settings.ToArray());
            // Chrome expects the server control stream to remain open after SETTINGS.
            await _localControlStream.WriteAsync(payload.ToArray(), completeWrites: false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)when (exception is QuicException or IOException)
        {
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogHTTPFailedToSendSETTINGS10058(_logger, exception);
            }
        }
    }

    private void DrainUnsupportedStreamInBackground(QuicStream stream, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await DrainUnsupportedStreamAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)when (exception is OperationCanceledException or QuicException or IOException)
            {
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
                {
                    LogHTTPUnidirectionalStreamEnded10059(_logger, exception);
                }
            }
        }, CancellationToken.None);
    }

    private async ValueTask DrainUnsupportedStreamAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        await using var ownedStream = stream.ConfigureAwait(false);
        var buffer = new byte[256];
        while (await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0)
        {
        }
    }

    private async ValueTask<Http3HeaderReadResult> ReadRequestHeadersAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        while (true)
        {
            var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!frame.Success)
            {
                return Http3HeaderReadResult.Failure("invalid_frame");
            }

            if (frame.Payload.Length > MaxFramePayloadBytes)
            {
                return Http3HeaderReadResult.Failure("frame_too_large");
            }

            if (frame.Type == Http3Codec.DataFrame)
            {
                return Http3HeaderReadResult.Failure("unexpected_data");
            }

            if (frame.Type is Http3Codec.SettingsFrame or Http3Codec.GoAwayFrame)
            {
                return Http3HeaderReadResult.Failure("unexpected_control_frame");
            }

            if (frame.Type != Http3Codec.HeadersFrame)
            {
                return Http3HeaderReadResult.Failure("unsupported_frame");
            }

            if (!Http3Codec.TryDecodeHeaderBlock(frame.Payload.Span, _listener.Http2Limits.MaxHeaderListBytes, out var headers, out var rejectionReason))
            {
                return Http3HeaderReadResult.Failure(rejectionReason);
            }

            return Http3HeaderReadResult.Successful(headers);
        }
    }

    private static async ValueTask<Http3FrameValidationResult> EnsureNoRequestBodyFramesAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        while (true)
        {
            var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!frame.Success)
            {
                return Http3FrameValidationResult.Successful();
            }

            if (frame.Type == Http3Codec.DataFrame)
            {
                return Http3FrameValidationResult.Failure("unexpected_data");
            }

            var reason = frame.Type == Http3Codec.HeadersFrame ? "duplicate_headers" : "unexpected_control_frame";
            return Http3FrameValidationResult.Failure(reason);
        }
    }

    private static async ValueTask<Http3FrameReadResult> ReadFrameAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        var type = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);
        var lengthResult = type.Success ? await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false) : Http3VarIntReadResult.Failure;
        if (!type.Success || !lengthResult.Success || lengthResult.Value < 0 || lengthResult.Value > MaxFramePayloadBytes)
        {
            return Http3FrameReadResult.Failure;
        }

        var length = lengthResult.Value;
        var buffer = new byte[(int)length];
        if (!await TryReadExactAsync(stream, buffer, cancellationToken).ConfigureAwait(false))
        {
            return Http3FrameReadResult.Failure;
        }

        return new Http3FrameReadResult(true, type.Value, buffer);
    }

    private static async ValueTask<Http3VarIntReadResult> ReadVarIntAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        var firstBuffer = new byte[1];
        var firstRead = await stream.ReadAsync(firstBuffer, cancellationToken).ConfigureAwait(false);
        if (firstRead == 0)
        {
            return Http3VarIntReadResult.Failure;
        }

        var first = firstBuffer[0];
        var length = 1 << (first >> 6);
        var value = first & 0x3f;
        if (length == 1)
        {
            return new Http3VarIntReadResult(true, value);
        }

        var rest = new byte[length - 1];
        if (!await TryReadExactAsync(stream, rest, cancellationToken).ConfigureAwait(false))
        {
            return Http3VarIntReadResult.Failure;
        }

        foreach (var next in rest)
        {
            value = (value << 8) | next;
        }

        return new Http3VarIntReadResult(true, value);
    }

    private static async ValueTask<bool> TryReadExactAsync(QuicStream stream, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var read = await stream.ReadAsync(destination[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            total += read;
        }

        return true;
    }

    private async ValueTask<ForwardingResult> ForwardWithRetriesAsync(QuicStream stream, Stream body, Http1RequestHead requestHead, RuntimeRoute route, RuntimeTimeouts timeouts, RuntimeConnectionLimits connectionLimits, RuntimeLimits limits, string upstreamTarget, ForwardedHeadersContext forwardedHeaders, ProxyRequestContext context, string requestId, CancellationToken cancellationToken)
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
                await WriteGeneratedResponseAsync(stream, failureResponse, context, requestHead.Method, cancellationToken).ConfigureAwait(false);
                return failureResponse.ToForwardingResult();
            }

            context.SetUpstream(ProxyRequestContextRuntimeMapper.ToRequestUpstream(selection.Upstream));
            var suppressGeneratedFailureResponse = ProxyRetryPolicy.ShouldSuppressAttemptFailureResponse(retryAllowed, attempt, maxAttempts);
            using var translator = new Http3ResponseTranslationStream(this, stream, requestHead.Method, _configurationSnapshot.Timeouts.DownstreamWriteTimeout, body);
            var result = await _forwarder.ForwardAsync(translator, Http1HeadReadResult.TranslatedRequestBody(ReadOnlyMemory<byte>.Empty), requestHead, route, selection.Upstream, _listener, ProxyTimeoutPolicy.ApplyRetryAttemptTimeout(ProxyTimeoutRuntimeMapper.ToPolicyInput(route), timeouts), connectionLimits, limits, upstreamTarget, forwardedHeaders, preferClientKeepAlive: false, requestId, cancellationToken, suppressGeneratedFailureResponse).ConfigureAwait(false);
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
                return await WriteSuppressedFailureAsync(stream, suppressedFailure, context, requestHead.Method, cancellationToken).ConfigureAwait(false);
            }

            await translator.CompleteAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        if (lastResult is ForwardingResult.FailureResult { ResponseStarted: false } lastFailure)
        {
            return await WriteSuppressedFailureAsync(stream, lastFailure, context, requestHead.Method, cancellationToken).ConfigureAwait(false);
        }

        return ProxyRetryPolicy.RequireCompletedAttemptResult(lastResult);
    }

    private async ValueTask<ForwardingResult> WriteSuppressedFailureAsync(QuicStream stream, ForwardingResult.FailureResult result, ProxyRequestContext context, string method, CancellationToken cancellationToken)
    {
        var response = ProxyGeneratedFailurePolicy.BuildFailureResponse(result);
        ProxyGeneratedFailureMetrics.Record(_metrics, response);
        await WriteGeneratedResponseAsync(stream, response, context, method, cancellationToken).ConfigureAwait(false);
        return response.ToForwardingResult();
    }

    private async ValueTask WriteCachedResponseAsync(QuicStream stream, Http1RequestHead requestHead, CachedProxyResponse response, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        var headers = ProxyCachedResponseHeaderPolicy.BuildFramedResponseHeaders(response, context.RequestId, _timeProvider.GetUtcNow());
        var includeBody = !string.Equals(requestHead.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
        await WriteHeadersAndBodyAsync(stream, response.StatusCode, headers, includeBody ? response.Body : [], cancellationToken).ConfigureAwait(false);
        context.RecordCachedResponse(response, keepClientConnectionOpen: true);
    }

    private async ValueTask WriteGeneratedRouteResponseAsync(QuicStream stream, GeneratedRouteResponse response, ProxyRequestContext context, string method, CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(response.Body);
        var headers = GeneratedRouteResponseHeaderPolicy.BuildFramedResponseHeaders(response, context.RequestId, bodyBytes.Length);
        await WriteHeadersAndBodyAsync(stream, response.StatusCode, headers, string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) ? [] : bodyBytes, cancellationToken).ConfigureAwait(false);
        context.RecordGeneratedRouteResponse(response, keepClientConnectionOpen: true);
    }

    private ValueTask WriteGeneratedResponseAsync(QuicStream stream, int statusCode, string body, ProxyRequestContext context, ProxyFailureKind failureKind, string method, CancellationToken cancellationToken)
    {
        return WriteGeneratedResponseAsync(stream, ProxyGeneratedFailurePolicy.BuildFailureResponse(statusCode, body, failureKind), context, method, cancellationToken);
    }

    private async ValueTask WriteGeneratedResponseAsync(QuicStream stream, ProxyGeneratedFailureResponse response, ProxyRequestContext context, string method, CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(response.Body);
        var headers = ProxyGeneratedFailurePolicy.BuildFramedResponseHeaders(response, context.RequestId, bodyBytes.Length);
        await WriteHeadersAndBodyAsync(stream, response.StatusCode, headers, string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) ? [] : bodyBytes, cancellationToken).ConfigureAwait(false);
        context.RecordGeneratedFailureResponse(response, keepClientConnectionOpen: true);
    }

    private async ValueTask WriteHeadersAndBodyAsync(QuicStream stream, int statusCode, IReadOnlyList<ProxyHeaderField> headers, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        await WriteHeadersAsync(stream, statusCode, headers, completeWrites: body.Length == 0, cancellationToken).ConfigureAwait(false);
        if (body.Length > 0)
        {
            await WriteDataAsync(stream, body, completeWrites: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask WriteHeadersAsync(QuicStream stream, int statusCode, IReadOnlyList<ProxyHeaderField> headers, bool completeWrites, CancellationToken cancellationToken)
    {
        List<ProxyHeaderField> encodedHeaders = [new(":status", Http3Codec.StatusText(statusCode))];
        foreach (var header in headers)
        {
            if (!header.Name.StartsWith(':') && !HopByHopHeaderPolicy.IsHopByHopHeader(header.Name))
            {
                // The native HTTP/3 response encoder must emit lowercase field names.
#pragma warning disable CA1308
                encodedHeaders.Add(new ProxyHeaderField(header.Name.ToLowerInvariant(), header.Value));
#pragma warning restore CA1308
            }
        }

        var headerBlock = Http3Codec.EncodeHeaderBlock(encodedHeaders);
        await WriteFrameAsync(stream, Http3Codec.HeadersFrame, headerBlock, completeWrites, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteDataAsync(QuicStream stream, ReadOnlyMemory<byte> body, bool completeWrites, CancellationToken cancellationToken)
    {
        var remaining = body;
        while (remaining.Length > 0)
        {
            var chunkLength = Math.Min(_listener.Http2Limits.MaxFrameSize, remaining.Length);
            var final = completeWrites && chunkLength == remaining.Length;
            await WriteFrameAsync(stream, Http3Codec.DataFrame, remaining[..chunkLength], final, cancellationToken).ConfigureAwait(false);
            _metrics.AddHttp3ResponseBytesSent(chunkLength);
            remaining = remaining[chunkLength..];
        }

        if (body.Length == 0 && completeWrites)
        {
            await WriteFrameAsync(stream, Http3Codec.DataFrame, ReadOnlyMemory<byte>.Empty, completeWrites: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask WriteFrameAsync(QuicStream stream, long frameType, ReadOnlyMemory<byte> payload, bool completeWrites, CancellationToken cancellationToken)
    {
        using var frame = new MemoryStream();
        Http3Codec.WriteFrame(frame, frameType, payload.Span);
        await stream.WriteAsync(frame.ToArray(), completeWrites, cancellationToken).ConfigureAwait(false);
        _metrics.AddBytesWritten(frame.Length);
    }

    private ProxyRequestContext CreateRequestContext()
    {
        return new ProxyRequestContext(_requestIdGenerator.Create(), _listener.Name, ProxyRequestContextRuntimeMapper.ToTransport(_listener), _connection.RemoteEndPoint?.ToString(), _configurationSnapshot.Version, _timeProvider, "http3");
    }

    private void CompleteContext(ref ProxyRequestContext? context)
    {
        if (context is null)
        {
            return;
        }

        _accessLogEmitter.Complete(context, context.AccessLogEnabled ?? _configurationSnapshot.Observability.AccessLogEnabled, _configurationSnapshot.Observability.RecentDiagnosticsCapacity);
        if (context.ResponseStatusCode.HasValue)
        {
            _metrics.Http3RequestCompleted(context.Method, context.ResponseStatusCode, Http3Outcome(context));
        }

        context = null;
    }

    private bool RecordProtocolError(string reason)
    {
        _metrics.Http3ProtocolError(reason);
        return Interlocked.Increment(ref _protocolErrors) >= MaxProtocolErrorsPerConnection;
    }

    private static string Http3Outcome(ProxyRequestContext context)
    {
        if (context.FailureKind != ProxyFailureKind.None)
        {
            return "failure";
        }

        if (!context.ResponseStatusCode.HasValue)
        {
            return "aborted";
        }

        return context.ResponseStatusCode.Value < 400 ? "success" : "error";
    }

    private static void ApplyForwardingResult(ProxyRequestContext context, ForwardingResult result)
    {
        context.RecordForwardingResult(result, keepClientConnectionOpen: true);
    }

    private sealed record Http3HeaderReadResult(bool Success, IReadOnlyList<ProxyHeaderField> Headers, string Reason)
    {
        public static Http3HeaderReadResult Successful(IReadOnlyList<ProxyHeaderField> headers)
        {
            return new Http3HeaderReadResult(true, headers, "");
        }

        public static Http3HeaderReadResult Failure(string reason)
        {
            return new Http3HeaderReadResult(false, [], reason);
        }
    }

    private readonly record struct Http3FrameValidationResult(bool Success, string Reason)
    {
        public static Http3FrameValidationResult Successful()
        {
            return new Http3FrameValidationResult(true, "");
        }

        public static Http3FrameValidationResult Failure(string reason)
        {
            return new Http3FrameValidationResult(false, reason);
        }
    }

    private readonly record struct Http3FrameReadResult(bool Success, long Type, ReadOnlyMemory<byte> Payload)
    {
        public static Http3FrameReadResult Failure { get; } = new(false, 0, ReadOnlyMemory<byte>.Empty);
    }

    private readonly record struct Http3VarIntReadResult(bool Success, long Value)
    {
        public static Http3VarIntReadResult Failure { get; } = new(false, 0);
    }

    private sealed class Http3RequestBodyReadStream : Stream
    {
        private readonly Http3Connection _connection;
        private readonly QuicStream _stream;
        private readonly Http1RequestFraming _framing;
        private readonly CancellationToken _connectionCancellationToken;
        private byte[] _pending = [];
        private int _pendingOffset;
        private long _remainingContentLength;
        private bool _completed;
        public Http3RequestBodyReadStream(Http3Connection connection, QuicStream stream, Http1RequestFraming framing, CancellationToken connectionCancellationToken)
        {
            _connection = connection;
            _stream = stream;
            _framing = framing;
            _connectionCancellationToken = connectionCancellationToken;
            _remainingContentLength = framing.ContentLength.GetValueOrDefault();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer.AsMemory(offset, count), _connectionCancellationToken).AsTask().GetAwaiter().GetResult();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0)
            {
                return 0;
            }

            if (_framing.Kind == Http1BodyKind.None)
            {
                return 0;
            }

            while (!HasPending())
            {
                if (!await FillPendingAsync(cancellationToken).ConfigureAwait(false))
                {
                    return 0;
                }
            }

            var count = Math.Min(buffer.Length, _pending.Length - _pendingOffset);
            _pending.AsMemory(_pendingOffset, count).CopyTo(buffer);
            _pendingOffset += count;
            if (_pendingOffset >= _pending.Length)
            {
                _pending = [];
                _pendingOffset = 0;
            }

            return count;
        }

        private async ValueTask<bool> FillPendingAsync(CancellationToken cancellationToken)
        {
            if (_completed)
            {
                return false;
            }

            if (_framing.Kind == Http1BodyKind.ContentLength && _remainingContentLength <= 0)
            {
                _completed = true;
                return false;
            }

            while (true)
            {
                var frame = await ReadFrameAsync(_stream, cancellationToken).ConfigureAwait(false);
                if (!frame.Success)
                {
                    if (_framing.Kind == Http1BodyKind.ContentLength && _remainingContentLength > 0)
                    {
                        throw new IOException("HTTP/3 stream ended before the declared request body was complete.");
                    }

                    _completed = true;
                    if (_framing.Kind == Http1BodyKind.Chunked)
                    {
                        _pending = "0\r\n\r\n"u8.ToArray();
                        _pendingOffset = 0;
                        return true;
                    }

                    return false;
                }

                if (frame.Type != Http3Codec.DataFrame)
                {
                    _connection._metrics.Http3ProtocolError(frame.Type == Http3Codec.HeadersFrame ? "duplicate_headers" : "unexpected_control_frame");
                    throw new IOException("HTTP/3 request body stream contained a non-DATA frame.");
                }

                if (frame.Payload.Length == 0)
                {
                    continue;
                }

                _connection._metrics.AddHttp3RequestBodyBytesReceived(frame.Payload.Length);
                if (_framing.Kind == Http1BodyKind.ContentLength)
                {
                    if (frame.Payload.Length > _remainingContentLength)
                    {
                        _connection._metrics.Http3ProtocolError("invalid_content_length");
                        throw new IOException("HTTP/3 request body exceeded the declared Content-Length.");
                    }

                    _remainingContentLength -= frame.Payload.Length;
                    _pending = frame.Payload.ToArray();
                    _pendingOffset = 0;
                    return true;
                }

                _pending = BuildChunk(frame.Payload.Span);
                _pendingOffset = 0;
                return true;
            }
        }

        private bool HasPending()
        {
            return _pendingOffset < _pending.Length;
        }

        private static byte[] BuildChunk(ReadOnlySpan<byte> payload)
        {
            var prefix = Encoding.ASCII.GetBytes(payload.Length.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            var chunk = new byte[prefix.Length + payload.Length + 2];
            prefix.CopyTo(chunk);
            payload.CopyTo(chunk.AsSpan(prefix.Length));
            chunk[^2] = (byte)'\r';
            chunk[^1] = (byte)'\n';
            return chunk;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Http3ResponseTranslationStream : Stream
    {
        private readonly Http3Connection _connection;
        private readonly QuicStream _stream;
        private readonly string _method;
        private readonly TimeSpan _writeTimeout;
        private readonly Stream _requestBody;
        private readonly MemoryStream _headBuffer = new();
        private readonly MemoryStream _chunkBuffer = new();
        private bool _headWritten;
        private bool _endStreamSent;
        private bool _dropBody;
        private bool _decodeChunkedBody;
        private bool _responseStreamActive;
        private ChunkParserState _chunkState = ChunkParserState.ReadingSize;
        private long _chunkBytesRemaining;
        public Http3ResponseTranslationStream(Http3Connection connection, QuicStream stream, string method, TimeSpan writeTimeout, Stream requestBody)
        {
            _connection = connection;
            _stream = stream;
            _method = method;
            _writeTimeout = writeTimeout;
            _requestBody = requestBody;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _headBuffer.Dispose();
                _chunkBuffer.Dispose();
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _requestBody.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _requestBody.ReadAsync(buffer, cancellationToken);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await WriteCoreAsync(buffer, timeoutToken).ConfigureAwait(false), _writeTimeout, ProxyTimeoutKind.DownstreamWrite, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)when (exception is QuicException or IOException)
            {
                _connection._metrics.Http3ResponseStreamReset();
                EndResponseStream();
                throw;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            if (_headWritten && !_endStreamSent)
            {
                await _connection.WriteDataAsync(_stream, ReadOnlyMemory<byte>.Empty, completeWrites: true, cancellationToken).ConfigureAwait(false);
                _endStreamSent = true;
            }

            EndResponseStream();
        }

        private async ValueTask WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            if (!_headWritten)
            {
                _headBuffer.Write(buffer.Span);
                var bytes = _headBuffer.ToArray();
                var split = IndexOfHeaderEnd(bytes);
                if (split < 0)
                {
                    return;
                }

                var bodyOffset = split + 4;
                if (!Http1ResponseParser.TryParse(bytes.AsSpan(0, bodyOffset), _method, out var responseHead, out var parseError))
                {
                    throw new IOException($"Invalid HTTP/1 response head: {parseError}.");
                }

                _dropBody = Http1ResponseParser.IsNoBodyResponse(_method, responseHead.StatusCode);
                _decodeChunkedBody = responseHead.Framing.Kind == Http1BodyKind.Chunked && !_dropBody;
                var headers = new HopByHopHeaderPolicy().FilterForForwarding(responseHead.Headers, preserveTransferEncoding: false, preserveTrailer: false);
                var bodyBytes = bytes.AsMemory(bodyOffset);
                var endWithHeaders = _dropBody || (!_decodeChunkedBody && responseHead.Framing.Kind == Http1BodyKind.None);
                await _connection.WriteHeadersAsync(_stream, responseHead.StatusCode, headers, completeWrites: endWithHeaders, cancellationToken).ConfigureAwait(false);
                _headWritten = true;
                _endStreamSent = endWithHeaders;
                _headBuffer.SetLength(0);
                if (!_dropBody && !_endStreamSent)
                {
                    StartResponseStream();
                }

                if (!_dropBody && bodyBytes.Length > 0 && !_endStreamSent)
                {
                    await WriteResponseBodyAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            if (!_dropBody && !_endStreamSent && buffer.Length > 0)
            {
                await WriteResponseBodyAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask WriteResponseBodyAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_decodeChunkedBody)
            {
                await WriteDecodedChunkedBodyAsync(buffer, cancellationToken).ConfigureAwait(false);
                return;
            }

            await _connection.WriteDataAsync(_stream, buffer, completeWrites: false, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask WriteDecodedChunkedBodyAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            _chunkBuffer.Position = _chunkBuffer.Length;
            _chunkBuffer.Write(buffer.Span);
            var bytes = _chunkBuffer.ToArray();
            var offset = 0;
            while (offset < bytes.Length && !_endStreamSent)
            {
                if (_chunkState == ChunkParserState.ReadingSize)
                {
                    var lineEnd = IndexOfCrlf(bytes, offset);
                    if (lineEnd < 0)
                    {
                        break;
                    }

                    var line = Encoding.ASCII.GetString(bytes, offset, lineEnd - offset);
                    var separator = line.IndexOf(';', StringComparison.Ordinal);
                    if (separator >= 0)
                    {
                        line = line[..separator];
                    }

                    if (!long.TryParse(line.Trim(), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _chunkBytesRemaining) || _chunkBytesRemaining < 0)
                    {
                        throw new IOException("Invalid chunked response body.");
                    }

                    offset = lineEnd + 2;
                    _chunkState = _chunkBytesRemaining == 0 ? ChunkParserState.ReadingTrailers : ChunkParserState.ReadingData;
                    continue;
                }

                if (_chunkState == ChunkParserState.ReadingData)
                {
                    var available = bytes.Length - offset;
                    if (available <= 0)
                    {
                        break;
                    }

                    var take = (int)Math.Min(available, _chunkBytesRemaining);
                    await _connection.WriteDataAsync(_stream, bytes.AsMemory(offset, take), completeWrites: false, cancellationToken).ConfigureAwait(false);
                    offset += take;
                    _chunkBytesRemaining -= take;
                    if (_chunkBytesRemaining == 0)
                    {
                        _chunkState = ChunkParserState.ReadingDataCrlf;
                    }

                    continue;
                }

                if (_chunkState == ChunkParserState.ReadingDataCrlf)
                {
                    if (bytes.Length - offset < 2)
                    {
                        break;
                    }

                    if (bytes[offset] != (byte)'\r' || bytes[offset + 1] != (byte)'\n')
                    {
                        throw new IOException("Invalid chunked response body.");
                    }

                    offset += 2;
                    _chunkState = ChunkParserState.ReadingSize;
                    continue;
                }

                if (_chunkState == ChunkParserState.ReadingTrailers)
                {
                    var lineEnd = IndexOfCrlf(bytes, offset);
                    if (lineEnd < 0)
                    {
                        break;
                    }

                    if (lineEnd == offset)
                    {
                        await _connection.WriteDataAsync(_stream, ReadOnlyMemory<byte>.Empty, completeWrites: true, cancellationToken).ConfigureAwait(false);
                        _endStreamSent = true;
                        EndResponseStream();
                        _chunkState = ChunkParserState.Complete;
                        offset = lineEnd + 2;
                        continue;
                    }

                    offset = lineEnd + 2;
                    continue;
                }

                offset = bytes.Length;
            }

            var remaining = bytes.AsMemory(offset).ToArray();
            _chunkBuffer.SetLength(0);
            _chunkBuffer.Write(remaining);
        }

        private void StartResponseStream()
        {
            if (_responseStreamActive)
            {
                return;
            }

            _responseStreamActive = true;
            _connection._metrics.Http3StreamedResponse();
            _connection._metrics.Http3ResponseStreamStarted();
        }

        private void EndResponseStream()
        {
            if (!_responseStreamActive)
            {
                return;
            }

            _responseStreamActive = false;
            _connection._metrics.Http3ResponseStreamEnded();
        }

        private static int IndexOfHeaderEnd(ReadOnlySpan<byte> bytes)
        {
            for (var index = 3; index < bytes.Length; index++)
            {
                if (bytes[index - 3] == (byte)'\r' && bytes[index - 2] == (byte)'\n' && bytes[index - 1] == (byte)'\r' && bytes[index] == (byte)'\n')
                {
                    return index - 3;
                }
            }

            return -1;
        }

        private static int IndexOfCrlf(ReadOnlySpan<byte> bytes, int start)
        {
            for (var index = start + 1; index < bytes.Length; index++)
            {
                if (bytes[index - 1] == (byte)'\r' && bytes[index] == (byte)'\n')
                {
                    return index - 1;
                }
            }

            return -1;
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private enum ChunkParserState
    {
        ReadingSize,
        ReadingData,
        ReadingDataCrlf,
        ReadingTrailers,
        Complete
    }
}
