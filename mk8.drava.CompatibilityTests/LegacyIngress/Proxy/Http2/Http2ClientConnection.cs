using Mk8.Drava.Application.INF.Proxy.Http2;
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
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.Application.INF.Proxy.Http1;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Http3;
using Mk8.Drava.Application.DAL.Observability;
using Mk8.Drava.Application.INF.Observability;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Http2;
public sealed class Http2ClientConnection
{
    private static readonly byte[] ClientPreface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
    private readonly Stream _stream;
    private readonly IPEndPoint? _remoteEndPoint;
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
    private readonly Http3AltSvcPolicy _altSvcPolicy;
    private readonly CircuitBreakerStore _circuitBreakerStore;
    private readonly AcmeHttp01ChallengeResponder _acmeChallengeResponder;
    private readonly ProxyMetrics _metrics;
    private readonly RequestIdGenerator _requestIdGenerator;
    private readonly AccessLogEmitter _accessLogEmitter;
    private readonly ClientRateLimiter _rateLimiter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<int, StreamState> _streams = new();
    public Http2ClientConnection(Stream stream, IPEndPoint? remoteEndPoint, ProxyConfigurationSnapshot configurationSnapshot, RuntimeListener listener, IRouteMatcher routeMatcher, IUpstreamSelector upstreamSelector, UpstreamHealthStore healthStore, ProxyForwarder forwarder, ForwardedHeadersPolicy forwardedHeadersPolicy, ProxyRouteActionPolicy routeActionPolicy, PathRewritePolicy pathRewritePolicy, ResponseCacheStore cacheStore, Http3AltSvcPolicy altSvcPolicy, CircuitBreakerStore circuitBreakerStore, AcmeHttp01ChallengeResponder acmeChallengeResponder, ProxyMetrics metrics, RequestIdGenerator requestIdGenerator, AccessLogEmitter accessLogEmitter, ClientRateLimiter rateLimiter, TimeProvider timeProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configurationSnapshot);
        _stream = stream;
        _remoteEndPoint = remoteEndPoint;
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
        _altSvcPolicy = altSvcPolicy;
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
        _metrics.Http2ConnectionAccepted();
        if (!await ReadPrefaceAsync(cancellationToken).ConfigureAwait(false))
        {
            _metrics.Http2ProtocolError("bad_preface");
            return;
        }

        await SendSettingsAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frame is null)
                {
                    return;
                }

                if (!await HandleFrameAsync(frame.Value, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (IOException exception)
        {
            _metrics.ClientPrematureDisconnect();
            _logger.LogDebug(exception, "HTTP/2 client connection ended with I/O failure.");
        }
    }

    private async ValueTask<bool> HandleFrameAsync(Http2Frame frame, CancellationToken cancellationToken)
    {
        switch (frame.Type)
        {
            case Http2FrameType.Data:
                return await HandleDataAsync(frame, cancellationToken).ConfigureAwait(false);
            case Http2FrameType.Headers:
                return await HandleHeadersAsync(frame, cancellationToken).ConfigureAwait(false);
            case Http2FrameType.Priority:
                return true;
            case Http2FrameType.RstStream:
                _streams.TryRemove(frame.StreamId, out _);
                return true;
            case Http2FrameType.Settings:
                if ((frame.Flags & Http2Flags.Ack) == 0)
                {
                    await WriteFrameAsync(Http2FrameType.Settings, Http2Flags.Ack, 0, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                }

                return true;
            case Http2FrameType.Ping:
                if (frame.Payload.Length == 8 && (frame.Flags & Http2Flags.Ack) == 0)
                {
                    await WriteFrameAsync(Http2FrameType.Ping, Http2Flags.Ack, 0, frame.Payload, cancellationToken).ConfigureAwait(false);
                }

                return true;
            case Http2FrameType.GoAway:
                return false;
            case Http2FrameType.WindowUpdate:
                return true;
            case Http2FrameType.Continuation:
                return await HandleContinuationAsync(frame, cancellationToken).ConfigureAwait(false);
            default:
                return true;
        }
    }

    private async ValueTask<bool> HandleHeadersAsync(Http2Frame frame, CancellationToken cancellationToken)
    {
        if (frame.StreamId <= 0 || frame.StreamId % 2 == 0)
        {
            _metrics.Http2ProtocolError("invalid_stream_id");
            await SendGoAwayAsync(Http2ErrorCode.ProtocolError, cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (_streams.Count(static pair => !pair.Value.Completed) >= _listener.Http2Limits.MaxConcurrentStreams)
        {
            _metrics.Http2ProtocolError("max_concurrent_streams");
            await WriteRstStreamAsync(frame.StreamId, Http2ErrorCode.RefusedStream, cancellationToken).ConfigureAwait(false);
            return true;
        }

        var stream = _streams.GetOrAdd(frame.StreamId, id => new StreamState(id));
        if (stream.HeadersComplete)
        {
            _metrics.Http2ProtocolError("duplicate_headers");
            await WriteRstStreamAsync(frame.StreamId, Http2ErrorCode.ProtocolError, cancellationToken).ConfigureAwait(false);
            return true;
        }

        var payload = StripHeaderPaddingAndPriority(frame, out var valid);
        if (!valid)
        {
            _metrics.Http2ProtocolError("invalid_headers");
            await WriteRstStreamAsync(frame.StreamId, Http2ErrorCode.ProtocolError, cancellationToken).ConfigureAwait(false);
            return true;
        }

        stream.HeaderBlock.Write(payload.Span);
        if (stream.HeaderBlock.Length > _listener.Http2Limits.MaxHeaderListBytes)
        {
            _metrics.Http2ProtocolError("header_list_too_large");
            await WriteRstStreamAsync(frame.StreamId, Http2ErrorCode.EnhanceYourCalm, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if ((frame.Flags & Http2Flags.EndHeaders) != 0)
        {
            stream.HeadersComplete = true;
        }

        if ((frame.Flags & Http2Flags.EndStream) != 0)
        {
            stream.EndStreamReceived = true;
        }

        await TryProcessCompleteStreamAsync(stream, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> HandleContinuationAsync(Http2Frame frame, CancellationToken cancellationToken)
    {
        if (!_streams.TryGetValue(frame.StreamId, out var stream) || stream.HeadersComplete)
        {
            _metrics.Http2ProtocolError("unexpected_continuation");
            await WriteRstStreamAsync(frame.StreamId, Http2ErrorCode.ProtocolError, cancellationToken).ConfigureAwait(false);
            return true;
        }

        stream.HeaderBlock.Write(frame.Payload.Span);
        if (stream.HeaderBlock.Length > _listener.Http2Limits.MaxHeaderListBytes)
        {
            _metrics.Http2ProtocolError("header_list_too_large");
            await WriteRstStreamAsync(frame.StreamId, Http2ErrorCode.EnhanceYourCalm, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if ((frame.Flags & Http2Flags.EndHeaders) != 0)
        {
            stream.HeadersComplete = true;
        }

        await TryProcessCompleteStreamAsync(stream, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> HandleDataAsync(Http2Frame frame, CancellationToken cancellationToken)
    {
        if (!_streams.TryGetValue(frame.StreamId, out var stream) || !stream.HeadersComplete)
        {
            _metrics.Http2ProtocolError("unexpected_data");
            await WriteRstStreamAsync(frame.StreamId, Http2ErrorCode.ProtocolError, cancellationToken).ConfigureAwait(false);
            return true;
        }

        var payload = StripDataPadding(frame, out var valid);
        if (!valid)
        {
            _metrics.Http2ProtocolError("invalid_data");
            await WriteRstStreamAsync(frame.StreamId, Http2ErrorCode.ProtocolError, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (stream.Body.Length + payload.Length > _configurationSnapshot.Limits.MaxRequestBodyBytes)
        {
            _metrics.RequestBodySizeRejected();
            await WriteGeneratedResponseAsync(frame.StreamId, 413, "Payload Too Large", CreateRequestContext(), ProxyFailureKind.RequestPayloadTooLarge, "GET", cancellationToken).ConfigureAwait(false);
            _streams.TryRemove(frame.StreamId, out _);
            return true;
        }

        stream.Body.Write(payload.Span);
        if ((frame.Flags & Http2Flags.EndStream) != 0)
        {
            stream.EndStreamReceived = true;
        }

        await TryProcessCompleteStreamAsync(stream, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask TryProcessCompleteStreamAsync(StreamState stream, CancellationToken cancellationToken)
    {
        if (!stream.HeadersComplete || !stream.EndStreamReceived || stream.ProcessingStarted)
        {
            return;
        }

        stream.ProcessingStarted = true;
        _metrics.Http2StreamStarted();
        try
        {
            await ProcessStreamAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            stream.Completed = true;
            _streams.TryRemove(stream.Id, out _);
            _metrics.Http2StreamEnded();
        }
    }

    private async ValueTask ProcessStreamAsync(StreamState stream, CancellationToken cancellationToken)
    {
        var context = CreateRequestContext();
        try
        {
            var requestBuildResult = BuildRequest(stream);
            if (requestBuildResult is not Http2RequestBuildResult.Accepted acceptedRequest)
            {
                var rejectionReason = ((Http2RequestBuildResult.Rejected)requestBuildResult).Reason;
                _metrics.Http2ProtocolError(rejectionReason);
                await WriteGeneratedResponseAsync(stream.Id, 400, "Bad Request", context, ProxyFailureKind.ClientMalformedRequest, "GET", cancellationToken).ConfigureAwait(false);
                CompleteContext(ref context);
                return;
            }

            var requestHead = acceptedRequest.RequestHead;
            _metrics.RequestReceived();
            _metrics.Http2RequestReceived();
            context.SetRequest(requestHead.Method, requestHead.Host, requestHead.Target, ProxyExternalRequestIdPolicy.Extract(requestHead));
            var forwardedHeaders = _forwardedHeadersPolicy.Build(requestHead, ProxyForwardedHeadersRuntimeMapper.ToListener(_listener), _configurationSnapshot.ForwardedHeaders, ProxyClientAddressPolicy.ToForwardedHeadersPeer(_remoteEndPoint));
            context.SetClientEndpoint(forwardedHeaders.ResolvedClientEndpoint);
            if (_rateLimiter.AcquireRequest(forwardedHeaders.ResolvedClientAddress, _configurationSnapshot.Limits.RequestsPerMinutePerIp) is ClientRateLimitDecision.RejectedResult)
            {
                await WriteGeneratedResponseAsync(stream.Id, 429, "Too Many Requests", context, ProxyFailureKind.RateLimited, requestHead.Method, cancellationToken).ConfigureAwait(false);
                CompleteContext(ref context);
                return;
            }

            if (ProxyRequestMethodPolicy.IsConnectTunnelMethod(requestHead.Method))
            {
                _metrics.UnsupportedRequestFramingRejected();
                await WriteGeneratedResponseAsync(stream.Id, 501, "Not Implemented", context, ProxyFailureKind.ClientMalformedRequest, requestHead.Method, cancellationToken).ConfigureAwait(false);
                CompleteContext(ref context);
                return;
            }

            if (_acmeChallengeResponder.CreateResponse(requestHead)is AcmeHttp01ChallengeResponseResult.HandledResult acmeChallengeResponse)
            {
                await WriteGeneratedRouteResponseAsync(stream.Id, acmeChallengeResponse.Response, context, requestHead.Method, cancellationToken).ConfigureAwait(false);
                CompleteContext(ref context);
                return;
            }

            var routeMatch = _routeMatcher.Match(_routeCandidates, ProxyRouteMatchRuntimeMapper.ToRequest(requestHead));
            if (routeMatch is null)
            {
                await WriteGeneratedResponseAsync(stream.Id, 404, "Not Found", context, ProxyFailureKind.NoMatchingRoute, requestHead.Method, cancellationToken).ConfigureAwait(false);
                CompleteContext(ref context);
                return;
            }

            var route = ProxyRouteMatchRuntimeMapper.SelectRoute(_configurationSnapshot.Routes, routeMatch);
            context.SetRoute(ProxyRequestContextRuntimeMapper.ToRequestRoute(route));
            if (await TryHandleGeneratedRouteActionAsync(stream.Id, route, requestHead, context, cancellationToken).ConfigureAwait(false))
            {
                CompleteContext(ref context);
                return;
            }

            if (await TryRejectKnownLengthRequestBodyAsync(stream.Id, route, requestHead, context, cancellationToken).ConfigureAwait(false))
            {
                CompleteContext(ref context);
                return;
            }

            var upstreamTarget = _pathRewritePolicy.Apply(ProxyPathRewriteRuntimeMapper.ToPolicyInput(route), requestHead.Target, requestHead.Path);
            var effectiveTimeouts = ProxyTimeoutPolicy.ApplyRouteTimeouts(ProxyTimeoutRuntimeMapper.ToPolicyInput(route), _configurationSnapshot.Timeouts);
            if (await TryHandleCacheHitAsync(stream.Id, route, requestHead, upstreamTarget, context, cancellationToken).ConfigureAwait(false))
            {
                CompleteContext(ref context);
                return;
            }

            var result = await ForwardWithRetriesAsync(stream.Id, stream.Body.ToArray(), requestHead, route, effectiveTimeouts, _configurationSnapshot.ConnectionLimits, _configurationSnapshot.Limits, upstreamTarget, forwardedHeaders, context, context.RequestId, cancellationToken).ConfigureAwait(false);
            ApplyForwardingResult(context, result);
            CompleteContext(ref context);
        }
        catch (Exception exception)when (exception is SocketException or IOException)
        {
            _metrics.ClientPrematureDisconnect();
            context.RecordClientDisconnect();
            CompleteContext(ref context);
        }
    }

    private async ValueTask<bool> TryHandleGeneratedRouteActionAsync(int streamId, RuntimeRoute route, Http1RequestHead requestHead, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        var actionDecision = _routeActionPolicy.Evaluate(ProxyRouteActionRuntimeMapper.ToPolicyInput(route, requestHead, _listener, isUpgradeRequest: false));
        if (actionDecision.ShouldProxy)
        {
            return false;
        }

        await WriteGeneratedRouteResponseAsync(streamId, actionDecision.Response!, context, requestHead.Method, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> TryRejectKnownLengthRequestBodyAsync(int streamId, RuntimeRoute route, Http1RequestHead requestHead, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        if (requestHead.Framing.Kind != Http1BodyKind.ContentLength || requestHead.Framing.ContentLength.GetValueOrDefault() <= route.ResolvedOptions.MaxRequestBodyBytes)
        {
            return false;
        }

        _metrics.RequestBodySizeRejected();
        await WriteGeneratedResponseAsync(streamId, 413, "Payload Too Large", context, ProxyFailureKind.RequestPayloadTooLarge, requestHead.Method, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> TryHandleCacheHitAsync(int streamId, RuntimeRoute route, Http1RequestHead requestHead, string upstreamTarget, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        var cacheLookup = _cacheStore.Get(ProxyCacheRuntimeMapper.ToRequestScope(route, _listener), requestHead, upstreamTarget);
        if (cacheLookup is not ProxyCacheLookupResult.HitResult cacheHit)
        {
            return false;
        }

        await WriteCachedResponseAsync(streamId, requestHead, cacheHit.Response, context, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private Http2RequestBuildResult BuildRequest(StreamState stream)
    {
        if (!HpackCodec.TryDecodeRequestHeaders(stream.HeaderBlock.ToArray(), out var headers, out var rejectionReason))
        {
            return Http2RequestBuildResult.Reject(rejectionReason);
        }

        Dictionary<string, string> pseudo = new(StringComparer.Ordinal);
        List<ProxyHeaderField> regularHeaders = [];
        var regularHeaderSeen = false;
        foreach (var header in headers)
        {
            if (header.Name.Length == 0)
            {
                return Http2RequestBuildResult.Reject("empty_header_name");
            }

            if (header.Name.Any(static character => char.IsAsciiLetterUpper(character)))
            {
                return Http2RequestBuildResult.Reject("uppercase_header_name");
            }

            if (header.Name[0] == ':')
            {
                if (regularHeaderSeen || pseudo.ContainsKey(header.Name) || !Http2HeaderPolicy.IsAllowedRequestPseudoHeader(header.Name))
                {
                    return Http2RequestBuildResult.Reject("invalid_pseudo_header");
                }

                pseudo[header.Name] = header.Value;
                continue;
            }

            regularHeaderSeen = true;
            if (Http2HeaderPolicy.IsForbiddenRequestHeader(header.Name, header.Value))
            {
                return Http2RequestBuildResult.Reject("forbidden_header");
            }

            regularHeaders.Add(new ProxyHeaderField(header.Name, header.Value));
        }

        if (!pseudo.TryGetValue(":method", out var method) || !pseudo.TryGetValue(":scheme", out var scheme) || !pseudo.TryGetValue(":path", out var target))
        {
            return Http2RequestBuildResult.Reject("missing_pseudo_header");
        }

        if (!string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return Http2RequestBuildResult.Reject("invalid_scheme");
        }

        if (pseudo.ContainsKey(":protocol") || ProxyRequestMethodPolicy.IsConnectTunnelMethod(method))
        {
            return Http2RequestBuildResult.Reject("extended_connect_unsupported");
        }

        if (!target.StartsWith('/'))
        {
            return Http2RequestBuildResult.Reject("invalid_path");
        }

        var authority = pseudo.TryGetValue(":authority", out var value) ? value : regularHeaders.FirstOrDefault(static header => string.Equals(header.Name, "host", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(authority))
        {
            return Http2RequestBuildResult.Reject("missing_authority");
        }

        var hostHeader = regularHeaders.FirstOrDefault(static header => string.Equals(header.Name, "host", StringComparison.OrdinalIgnoreCase));
        if (hostHeader is not null && !string.Equals(hostHeader.Value, authority, StringComparison.OrdinalIgnoreCase))
        {
            return Http2RequestBuildResult.Reject("authority_host_mismatch");
        }

        regularHeaders.RemoveAll(static header => string.Equals(header.Name, "host", StringComparison.OrdinalIgnoreCase));
        regularHeaders.Insert(0, new ProxyHeaderField("Host", authority));
        var bodyLength = stream.Body.Length;
        var framing = bodyLength > 0 ? Http1RequestFraming.FromContentLength(bodyLength) : Http1RequestFraming.None;
        if (bodyLength > 0)
        {
            regularHeaders.Add(new ProxyHeaderField("Content-Length", bodyLength.ToString(CultureInfo.InvariantCulture)));
        }

        return Http2RequestBuildResult.Accept(new Http1RequestHead(method, target, ExtractPath(target), "HTTP/2", authority, framing, regularHeaders));
    }

    private abstract record Http2RequestBuildResult
    {
        private Http2RequestBuildResult()
        {
        }

        public static Accepted Accept(Http1RequestHead requestHead)
        {
            return new Accepted(requestHead);
        }

        public static Rejected Reject(string reason)
        {
            return new Rejected(reason);
        }

        public sealed record Accepted : Http2RequestBuildResult
        {
            public Accepted(Http1RequestHead requestHead)
            {
                ArgumentNullException.ThrowIfNull(requestHead);
                RequestHead = requestHead;
            }

            public Http1RequestHead RequestHead { get; }
        }

        public sealed record Rejected : Http2RequestBuildResult
        {
            public Rejected(string reason)
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new ArgumentException("HTTP/2 request rejection reason is required.", nameof(reason));
                }

                Reason = reason;
            }

            public string Reason { get; }
        }
    }

    private async ValueTask<ForwardingResult> ForwardWithRetriesAsync(int streamId, byte[] body, Http1RequestHead requestHead, RuntimeRoute route, RuntimeTimeouts timeouts, RuntimeConnectionLimits connectionLimits, RuntimeLimits limits, string upstreamTarget, ForwardedHeadersContext forwardedHeaders, ProxyRequestContext context, string requestId, CancellationToken cancellationToken)
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
                await WriteGeneratedResponseAsync(streamId, failureResponse, context, requestHead.Method, cancellationToken).ConfigureAwait(false);
                return failureResponse.ToForwardingResult();
            }

            context.SetUpstream(ProxyRequestContextRuntimeMapper.ToRequestUpstream(selection.Upstream));
            var suppressGeneratedFailureResponse = ProxyRetryPolicy.ShouldSuppressAttemptFailureResponse(retryAllowed, attempt, maxAttempts);
            var translator = new Http2ResponseTranslationStream(this, streamId, requestHead.Method, _configurationSnapshot.Timeouts.DownstreamWriteTimeout, body);
            var result = await _forwarder.ForwardAsync(translator, Http1HeadReadResult.TranslatedRequestBody(body), requestHead, route, selection.Upstream, _listener, ProxyTimeoutPolicy.ApplyRetryAttemptTimeout(ProxyTimeoutRuntimeMapper.ToPolicyInput(route), timeouts), connectionLimits, limits, upstreamTarget, forwardedHeaders, preferClientKeepAlive: false, requestId, cancellationToken, suppressGeneratedFailureResponse).ConfigureAwait(false);
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
                return await WriteSuppressedFailureAsync(streamId, suppressedFailure, context, requestHead.Method, cancellationToken).ConfigureAwait(false);
            }

            await translator.CompleteAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        if (lastResult is ForwardingResult.FailureResult { ResponseStarted: false } lastFailure)
        {
            return await WriteSuppressedFailureAsync(streamId, lastFailure, context, requestHead.Method, cancellationToken).ConfigureAwait(false);
        }

        return ProxyRetryPolicy.RequireCompletedAttemptResult(lastResult);
    }

    private async ValueTask<ForwardingResult> WriteSuppressedFailureAsync(int streamId, ForwardingResult.FailureResult result, ProxyRequestContext context, string method, CancellationToken cancellationToken)
    {
        var response = ProxyGeneratedFailurePolicy.BuildFailureResponse(result);
        ProxyGeneratedFailureMetrics.Record(_metrics, response);
        await WriteGeneratedResponseAsync(streamId, response, context, method, cancellationToken).ConfigureAwait(false);
        return response.ToForwardingResult();
    }

    private async ValueTask WriteCachedResponseAsync(int streamId, Http1RequestHead requestHead, CachedProxyResponse response, ProxyRequestContext context, CancellationToken cancellationToken)
    {
        var headers = ProxyCachedResponseHeaderPolicy.BuildFramedResponseHeaders(response, context.RequestId, _timeProvider.GetUtcNow()).ToList();
        AddAltSvcHeader(headers);
        var includeBody = !string.Equals(requestHead.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
        await WriteHeadersAndBodyAsync(streamId, response.StatusCode, headers, includeBody ? response.Body : [], cancellationToken).ConfigureAwait(false);
        context.RecordCachedResponse(response, keepClientConnectionOpen: true);
    }

    private async ValueTask WriteGeneratedRouteResponseAsync(int streamId, GeneratedRouteResponse response, ProxyRequestContext context, string method, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(response.Body);
        var headers = GeneratedRouteResponseHeaderPolicy.BuildFramedResponseHeaders(response, context.RequestId, body.Length).ToList();
        AddAltSvcHeader(headers);
        await WriteHeadersAndBodyAsync(streamId, response.StatusCode, headers, string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) ? [] : body, cancellationToken).ConfigureAwait(false);
        context.RecordGeneratedRouteResponse(response, keepClientConnectionOpen: true);
    }

    private async ValueTask WriteGeneratedResponseAsync(int streamId, int statusCode, string body, ProxyRequestContext context, ProxyFailureKind failureKind, string method, CancellationToken cancellationToken)
    {
        await WriteGeneratedResponseAsync(streamId, ProxyGeneratedFailurePolicy.BuildFailureResponse(statusCode, body, failureKind), context, method, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteGeneratedResponseAsync(int streamId, ProxyGeneratedFailureResponse response, ProxyRequestContext context, string method, CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(response.Body);
        var headers = ProxyGeneratedFailurePolicy.BuildFramedResponseHeaders(response, context.RequestId, bodyBytes.Length).ToList();
        AddAltSvcHeader(headers);
        await WriteHeadersAndBodyAsync(streamId, response.StatusCode, headers, string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) ? [] : bodyBytes, cancellationToken).ConfigureAwait(false);
        context.RecordGeneratedFailureResponse(response, keepClientConnectionOpen: true);
    }

    private async ValueTask WriteHeadersAndBodyAsync(int streamId, int statusCode, IReadOnlyList<ProxyHeaderField> headers, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var headerBlock = HpackCodec.EncodeResponseHeaders(statusCode, headers);
        await WriteFrameAsync(Http2FrameType.Headers, body.Length == 0 ? (byte)(Http2Flags.EndHeaders | Http2Flags.EndStream) : Http2Flags.EndHeaders, streamId, headerBlock, cancellationToken).ConfigureAwait(false);
        if (body.Length > 0)
        {
            await WriteDataAsync(streamId, body, endStream: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private void AddAltSvcHeader(List<ProxyHeaderField> headers)
    {
        var projected = Http3AltSvcPolicy.ApplyHeader(headers, _altSvcPolicy.CreateHeader(ProxyHttp3AltSvcRuntimeMapper.ToListenerInput(_listener)));
        headers.Clear();
        headers.AddRange(projected);
    }

    private async ValueTask WriteDataAsync(int streamId, ReadOnlyMemory<byte> body, bool endStream, CancellationToken cancellationToken)
    {
        var remaining = body;
        while (remaining.Length > 0)
        {
            var chunkLength = Math.Min(_listener.Http2Limits.MaxFrameSize, remaining.Length);
            var final = chunkLength == remaining.Length && endStream;
            await WriteFrameAsync(Http2FrameType.Data, final ? Http2Flags.EndStream : (byte)0, streamId, remaining[..chunkLength], cancellationToken).ConfigureAwait(false);
            remaining = remaining[chunkLength..];
        }

        if (body.Length == 0 && endStream)
        {
            await WriteFrameAsync(Http2FrameType.Data, Http2Flags.EndStream, streamId, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask SendSettingsAsync(CancellationToken cancellationToken)
    {
        var payload = new byte[18];
        WriteSetting(payload.AsSpan(0, 6), 3, (uint)_listener.Http2Limits.MaxConcurrentStreams);
        WriteSetting(payload.AsSpan(6, 6), 5, (uint)_listener.Http2Limits.MaxFrameSize);
        WriteSetting(payload.AsSpan(12, 6), 6, (uint)_listener.Http2Limits.MaxHeaderListBytes);
        await WriteFrameAsync(Http2FrameType.Settings, 0, 0, payload, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SendGoAwayAsync(Http2ErrorCode error, CancellationToken cancellationToken)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4, 4), (uint)error);
        await WriteFrameAsync(Http2FrameType.GoAway, 0, 0, payload, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask WriteRstStreamAsync(int streamId, Http2ErrorCode error, CancellationToken cancellationToken)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)error);
        return WriteFrameAsync(Http2FrameType.RstStream, 0, streamId, payload, cancellationToken);
    }

    private async ValueTask WriteFrameAsync(Http2FrameType type, byte flags, int streamId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var header = new byte[9];
        header[0] = (byte)((payload.Length >> 16) & 0xff);
        header[1] = (byte)((payload.Length >> 8) & 0xff);
        header[2] = (byte)(payload.Length & 0xff);
        header[3] = (byte)type;
        header[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(5, 4), (uint)streamId & 0x7fffffff);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            if (payload.Length > 0)
            {
                await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async ValueTask<Http2Frame?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(9, cancellationToken).ConfigureAwait(false);
        if (header.Length == 0)
        {
            return null;
        }

        var length = header[0] << 16 | header[1] << 8 | header[2];
        if (length > _listener.Http2Limits.MaxFrameSize)
        {
            _metrics.Http2ProtocolError("frame_too_large");
            await SendGoAwayAsync(Http2ErrorCode.FrameSizeError, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var type = (Http2FrameType)header[3];
        var flags = header[4];
        var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5, 4)) & 0x7fffffff);
        var payload = length == 0 ? [] : await ReadExactAsync(length, cancellationToken).ConfigureAwait(false);
        return new Http2Frame(type, flags, streamId, payload);
    }

    private async ValueTask<byte[]> ReadExactAsync(int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return offset == 0 ? [] : throw new IOException("HTTP/2 peer closed mid-frame.");
            }

            offset += read;
            _metrics.AddBytesRead(read);
        }

        return buffer;
    }

    private async ValueTask<bool> ReadPrefaceAsync(CancellationToken cancellationToken)
    {
        var preface = await ReadExactAsync(ClientPreface.Length, cancellationToken).ConfigureAwait(false);
        return preface.AsSpan().SequenceEqual(ClientPreface);
    }

    private ProxyRequestContext CreateRequestContext()
    {
        return new ProxyRequestContext(_requestIdGenerator.Create(), _listener.Name, ProxyRequestContextRuntimeMapper.ToTransport(_listener), _remoteEndPoint?.ToString(), _configurationSnapshot.Version, _timeProvider, "http2");
    }

    private void CompleteContext(ref ProxyRequestContext context)
    {
        _accessLogEmitter.Complete(context, context.AccessLogEnabled ?? _configurationSnapshot.Observability.AccessLogEnabled, _configurationSnapshot.Observability.RecentDiagnosticsCapacity);
    }

    private static void ApplyForwardingResult(ProxyRequestContext context, ForwardingResult result)
    {
        context.RecordForwardingResult(result, keepClientConnectionOpen: true);
    }

    private static string ExtractPath(string target)
    {
        var queryIndex = target.IndexOf('?');
        return queryIndex < 0 ? target : target[..queryIndex];
    }

    private static ReadOnlyMemory<byte> StripHeaderPaddingAndPriority(Http2Frame frame, out bool valid)
    {
        valid = true;
        var payload = frame.Payload;
        if ((frame.Flags & Http2Flags.Padded) != 0)
        {
            if (payload.Length == 0)
            {
                valid = false;
                return ReadOnlyMemory<byte>.Empty;
            }

            var padding = payload.Span[0];
            payload = payload[1..];
            if (padding > payload.Length)
            {
                valid = false;
                return ReadOnlyMemory<byte>.Empty;
            }

            payload = payload[..^padding];
        }

        if ((frame.Flags & Http2Flags.Priority) != 0)
        {
            if (payload.Length < 5)
            {
                valid = false;
                return ReadOnlyMemory<byte>.Empty;
            }

            payload = payload[5..];
        }

        return payload;
    }

    private static ReadOnlyMemory<byte> StripDataPadding(Http2Frame frame, out bool valid)
    {
        valid = true;
        var payload = frame.Payload;
        if ((frame.Flags & Http2Flags.Padded) == 0)
        {
            return payload;
        }

        if (payload.Length == 0)
        {
            valid = false;
            return ReadOnlyMemory<byte>.Empty;
        }

        var padding = payload.Span[0];
        payload = payload[1..];
        if (padding > payload.Length)
        {
            valid = false;
            return ReadOnlyMemory<byte>.Empty;
        }

        return payload[..^padding];
    }

    private static void WriteSetting(Span<byte> destination, ushort id, uint value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination[..2], id);
        BinaryPrimitives.WriteUInt32BigEndian(destination[2..], value);
    }

    private sealed class Http2ResponseTranslationStream : Stream
    {
        private readonly Http2ClientConnection _connection;
        private readonly int _streamId;
        private readonly string _method;
        private readonly TimeSpan _writeTimeout;
        private readonly MemoryStream _requestBody;
        private readonly MemoryStream _headBuffer = new();
        private bool _headWritten;
        private bool _endStreamSent;
        private bool _dropBody;
        public Http2ResponseTranslationStream(Http2ClientConnection connection, int streamId, string method, TimeSpan writeTimeout, byte[] requestBody)
        {
            _connection = connection;
            _streamId = streamId;
            _method = method;
            _writeTimeout = writeTimeout;
            _requestBody = new MemoryStream(requestBody, writable: false);
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
            await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await WriteCoreAsync(buffer, timeoutToken).ConfigureAwait(false), _writeTimeout, ProxyTimeoutKind.DownstreamWrite, cancellationToken).ConfigureAwait(false);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            if (_headWritten && !_endStreamSent)
            {
                await _connection.WriteDataAsync(_streamId, ReadOnlyMemory<byte>.Empty, endStream: true, cancellationToken).ConfigureAwait(false);
                _endStreamSent = true;
            }
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
                var headers = new HopByHopHeaderPolicy().FilterForForwarding(responseHead.Headers, preserveTransferEncoding: false, preserveTrailer: false);
                var bodyBytes = bytes.AsMemory(bodyOffset);
                var endWithHeaders = _dropBody || responseHead.Framing.Kind == Http1BodyKind.None;
                await _connection.WriteFrameAsync(Http2FrameType.Headers, endWithHeaders ? (byte)(Http2Flags.EndHeaders | Http2Flags.EndStream) : Http2Flags.EndHeaders, _streamId, HpackCodec.EncodeResponseHeaders(responseHead.StatusCode, headers), cancellationToken).ConfigureAwait(false);
                _headWritten = true;
                _endStreamSent = endWithHeaders;
                _headBuffer.SetLength(0);
                if (!_dropBody && bodyBytes.Length > 0 && !_endStreamSent)
                {
                    await _connection.WriteDataAsync(_streamId, bodyBytes, endStream: false, cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            if (!_dropBody && !_endStreamSent && buffer.Length > 0)
            {
                await _connection.WriteDataAsync(_streamId, buffer, endStream: false, cancellationToken).ConfigureAwait(false);
            }
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

    private sealed class StreamState
    {
        public StreamState(int id)
        {
            Id = id;
        }

        public int Id { get; }
        public MemoryStream HeaderBlock { get; } = new();
        public MemoryStream Body { get; } = new();
        public bool HeadersComplete { get; set; }
        public bool EndStreamReceived { get; set; }
        public bool ProcessingStarted { get; set; }
        public bool Completed { get; set; }
    }

    private readonly record struct HeaderField(string Name, string Value);
    private readonly record struct Http2Frame(Http2FrameType Type, byte Flags, int StreamId, ReadOnlyMemory<byte> Payload);
    private enum Http2FrameType : byte
    {
        Data = 0x0,
        Headers = 0x1,
        Priority = 0x2,
        RstStream = 0x3,
        Settings = 0x4,
        PushPromise = 0x5,
        Ping = 0x6,
        GoAway = 0x7,
        WindowUpdate = 0x8,
        Continuation = 0x9
    }

    private enum Http2ErrorCode : uint
    {
        NoError = 0,
        ProtocolError = 1,
        InternalError = 2,
        FlowControlError = 3,
        SettingsTimeout = 4,
        StreamClosed = 5,
        FrameSizeError = 6,
        RefusedStream = 7,
        Cancel = 8,
        CompressionError = 9,
        ConnectError = 10,
        EnhanceYourCalm = 11
    }

    private static class Http2Flags
    {
        public const byte EndStream = 0x1;
        public const byte Ack = 0x1;
        public const byte EndHeaders = 0x4;
        public const byte Padded = 0x8;
        public const byte Priority = 0x20;
    }
}
