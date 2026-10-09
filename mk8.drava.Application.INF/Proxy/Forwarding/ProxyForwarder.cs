using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.Application.INF.Proxy.Http1;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Mk8.Drava.Application.INF.Proxy.Exchange;
using Mk8.Drava.Application.INF.Proxy.Streams;
using Mk8.Drava.Application.INF.Proxy;
using Mk8.Drava.Application.DAL.Observability;
using Mk8.Drava.Application.INF.Observability;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;
public sealed partial class ProxyForwarder
{
    private readonly UpstreamConnectionPool _upstreamConnections;
    private readonly Http3UpstreamConnectionPool _http3UpstreamConnections;
    private readonly ProxyMetrics _metrics;
    private readonly HopByHopHeaderPolicy _headerPolicy;
    private readonly ResponseCacheStore _cacheStore;
    private readonly Http3AltSvcPolicy _altSvcPolicy;
    private readonly ILogger<ProxyForwarder> _logger;
    public ProxyForwarder(UpstreamConnectionPool upstreamConnections, Http3UpstreamConnectionPool http3UpstreamConnections, ProxyMetrics metrics, HopByHopHeaderPolicy headerPolicy, ResponseCacheStore cacheStore, Http3AltSvcPolicy altSvcPolicy, ILogger<ProxyForwarder> logger)
    {
        _upstreamConnections = upstreamConnections;
        _http3UpstreamConnections = http3UpstreamConnections;
        _metrics = metrics;
        _headerPolicy = headerPolicy;
        _cacheStore = cacheStore;
        _altSvcPolicy = altSvcPolicy;
        _logger = logger;
    }

    public async ValueTask<ForwardingResult> ForwardAsync(Stream clientStream, Http1HeadReadResult requestHeadRead, Http1RequestHead requestHead, RuntimeRoute route, RuntimeUpstream upstream, RuntimeListener listener, RuntimeTimeouts timeouts, RuntimeConnectionLimits connectionLimits, RuntimeLimits limits, string upstreamTarget, ForwardedHeadersContext forwardedHeaders, bool preferClientKeepAlive, string requestId, CancellationToken cancellationToken, bool suppressGeneratedFailureResponse = false)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        ArgumentNullException.ThrowIfNull(requestHeadRead);
        ArgumentNullException.ThrowIfNull(timeouts);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(requestHead);
        var responseStarted = false;
        UpstreamConnectionLease? upstreamLease = null;
        try
        {
            Http1BodyReader? preReadRequestBodyReader = null;
            byte[]? preReadChunkLine = null;
            if (requestHead.Framing.Kind == Http1BodyKind.Chunked && clientStream is not ExchangeClientStream)
            {
                preReadRequestBodyReader = new Http1BodyReader(clientStream, requestHeadRead.InitialBodyBytes, _metrics, timeouts.ClientRequestBodyIdleTimeout, ProxyTimeoutKind.ClientRequestBodyIdle);
                preReadChunkLine = await preReadRequestBodyReader.ReadLineWithCrlfAsync(listener.MaxChunkLineBytes, cancellationToken).ConfigureAwait(false);
                if (!Http1ChunkSizeParser.TryParseLine(preReadChunkLine.AsSpan(), out _))
                {
                    throw new Http1ClientProtocolException("Invalid chunk-size line.");
                }
            }

            ResponseForwardingResult responseResult;
            if (RuntimeUpstreamProtocol.IsHttp3(upstream.Protocol))
            {
                responseResult = await ForwardHttp3Async(clientStream, requestHeadRead, requestHead, route, upstream, listener, timeouts, connectionLimits, upstreamTarget, forwardedHeaders, preferClientKeepAlive, requestId, suppressGeneratedFailureResponse, preReadRequestBodyReader, preReadChunkLine, () => responseStarted = true, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                upstreamLease = await _upstreamConnections.BorrowAsync(upstream, timeouts, connectionLimits, cancellationToken).ConfigureAwait(false);
                var upstreamStream = upstreamLease.Stream;
                responseResult = RuntimeUpstreamProtocol.IsHttp2(upstream.Protocol) ? await ForwardHttp2Async(upstreamStream, clientStream, requestHeadRead, requestHead, route, upstream, listener, timeouts, upstreamTarget, forwardedHeaders, preferClientKeepAlive, requestId, suppressGeneratedFailureResponse, preReadRequestBodyReader, preReadChunkLine, () => responseStarted = true, cancellationToken).ConfigureAwait(false) : await ForwardHttp1Async(upstreamStream, clientStream, requestHeadRead, requestHead, route, listener, timeouts, upstreamTarget, forwardedHeaders, preferClientKeepAlive, requestId, suppressGeneratedFailureResponse, preReadRequestBodyReader, preReadChunkLine, () => responseStarted = true, cancellationToken).ConfigureAwait(false);
            }

            responseStarted = responseResult.ResponseStarted;
            if (responseResult.SuppressedForRetry)
            {
                _metrics.UpstreamFailed();
                return ForwardingResult.Failure(responseStarted: false, responseStatusCode: responseResult.StatusCode, failureKind: ProxyFailureKind.UpstreamUnavailable);
            }

            if (responseResult.CanReuseUpstreamConnection && upstreamLease is not null)
            {
                upstreamLease.MarkReusable();
            }

            _metrics.UpstreamSucceeded();
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogProxiedToUpstream10013(_logger, requestHead.Method, requestHead.Target, upstream.Name, null);
            }
            return ForwardingResult.Success(responseStarted, responseResult.KeepClientConnectionOpen, responseResult.StatusCode);
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProxyTimeoutException exception)
        {
            var timeoutFailure = ProxyTimeoutFailurePolicy.ClassifyForwardingTimeout(exception.Kind, responseStarted);
            await HandleTimeoutAsync(clientStream, requestHead, upstream, responseStarted, exception, timeouts, requestId, cancellationToken, suppressGeneratedFailureResponse).ConfigureAwait(false);
            return ForwardingResult.Failure(responseStarted, timeoutFailure.ResponseStatusCode, timeoutFailure.FailureKind);
        }
        catch (Http1PayloadTooLargeException exception)
        {
            _metrics.RequestBodySizeRejected();
            _metrics.ClientBodyRelayFailed();
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogRejectedOversizedRequestBodyFor10014(_logger, requestHead.Method, requestHead.Target, exception);
            }
            if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse))
            {
                await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.RequestPayloadTooLarge, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
            }

            return ForwardingResult.Failure(responseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted, ProxyFailureKind.RequestPayloadTooLarge), ProxyFailureKind.RequestPayloadTooLarge);
        }
        catch (Http1ClientProtocolException exception)
        {
            _metrics.MalformedRequestRejected();
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogRejectedMalformedRequestBodyFor10015(_logger, requestHead.Method, requestHead.Target, exception);
            }
            if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse))
            {
                await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.ClientMalformedRequest, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
            }

            return ForwardingResult.Failure(responseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted, ProxyFailureKind.ClientMalformedRequest), ProxyFailureKind.ClientMalformedRequest);
        }
        catch (Exception exception) when (exception is Http1UpstreamProtocolException or FramedUpstreamProtocolException)
        {
            _metrics.UpstreamMalformedResponse();
            _metrics.UpstreamFailed();
            if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse))
            {
                _metrics.UpstreamConnectFailed();
            }

            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
            {
                LogUpstreamResponseFramingFailedFor10016(_logger, requestHead.Method, requestHead.Target, upstream.Name, exception);
            }
            if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse))
            {
                await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.UpstreamMalformedResponse, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
            }

            return ForwardingResult.Failure(responseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted, ProxyFailureKind.UpstreamMalformedResponse), ProxyFailureKind.UpstreamMalformedResponse);
        }
        catch (Http2UpstreamProtocolException exception)
        {
            _metrics.UpstreamHttp2ProtocolError();
            _metrics.UpstreamMalformedResponse();
            _metrics.UpstreamFailed();
            if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse))
            {
                _metrics.UpstreamConnectFailed();
            }

            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
            {
                LogUpstreamHTTPResponseFramingFailed10017(_logger, requestHead.Method, requestHead.Target, upstream.Name, exception);
            }
            if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse))
            {
                await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.UpstreamMalformedResponse, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
            }

            return ForwardingResult.Failure(responseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted, ProxyFailureKind.UpstreamMalformedResponse), ProxyFailureKind.UpstreamMalformedResponse);
        }
        catch (Http3UpstreamProtocolException exception)
        {
            _metrics.UpstreamHttp3ProtocolError(exception.FailureKind == Http3UpstreamFailureKind.ConnectFailure ? "connect_failure" : "protocol_failure");
            _metrics.UpstreamFailed();
            var failureKind = exception.FailureKind == Http3UpstreamFailureKind.ConnectFailure && !responseStarted ? ProxyFailureKind.UpstreamConnectFailed : responseStarted ? ProxyFailureKind.UpstreamPrematureDisconnect : ProxyFailureKind.UpstreamMalformedResponse;
            if (failureKind == ProxyFailureKind.UpstreamMalformedResponse)
            {
                _metrics.UpstreamMalformedResponse();
            }

            if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse))
            {
                _metrics.UpstreamConnectFailed();
            }

            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
            {
                LogUpstreamHTTPForwardingFailedFor10018(_logger, requestHead.Method, requestHead.Target, upstream.Name, exception);
            }
            if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse))
            {
                await ProxyGeneratedFailureWriter.WriteAsync(clientStream, failureKind, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
            }

            return ForwardingResult.Failure(responseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted, failureKind), failureKind);
        }
        catch (UpstreamTlsException exception)
        {
            _metrics.UpstreamFailed();
            if (RuntimeUpstreamProtocol.IsHttp2(upstream.Protocol) && exception.Message.Contains("ALPN", StringComparison.OrdinalIgnoreCase))
            {
                _metrics.UpstreamHttp2AlpnFailed();
            }

            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
            {
                LogUpstreamTLSFailedForTo10019(_logger, requestHead.Method, requestHead.Target, upstream.Name, exception);
            }
            if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse))
            {
                await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.UpstreamConnectFailed, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
            }

            var failureKind = responseStarted ? ProxyFailureKind.UpstreamPrematureDisconnect : ProxyFailureKind.UpstreamConnectFailed;
            return ForwardingResult.Failure(responseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted, failureKind), failureKind);
        }
        catch (Exception exception)when (exception is SocketException or IOException)
        {
            _metrics.UpstreamFailed();
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
            {
                LogUpstreamForwardingFailedForTo10020(_logger, requestHead.Method, requestHead.Target, upstream.Name, exception);
            }
            if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse))
            {
                await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.UpstreamConnectFailed, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
            }

            var failureKind = responseStarted ? ProxyFailureKind.UpstreamPrematureDisconnect : ProxyFailureKind.UpstreamConnectFailed;
            return ForwardingResult.Failure(responseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted, failureKind), failureKind);
        }
        finally
        {
            if (upstreamLease is not null)
            {
                await upstreamLease.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<ResponseForwardingResult> ForwardHttp1Async(Stream upstreamStream, Stream clientStream, Http1HeadReadResult requestHeadRead, Http1RequestHead requestHead, RuntimeRoute route, RuntimeListener listener, RuntimeTimeouts timeouts, string upstreamTarget, ForwardedHeadersContext forwardedHeaders, bool preferClientKeepAlive, string requestId, bool suppressGeneratedFailureResponse, Http1BodyReader? preReadRequestBodyReader, byte[]? preReadChunkLine, Action markResponseStarted, CancellationToken cancellationToken)
    {
        await WriteRequestHeadAsync(upstreamStream, requestHead, route, upstreamTarget, forwardedHeaders, timeouts, cancellationToken).ConfigureAwait(false);
        // Preserve the copied native-ingress reference contract. Production exchanges use concurrent upload/response handling below.
        if (clientStream is not ExchangeClientStream exchange)
        {
            await RelayRequestBodyAsync(clientStream, upstreamStream, requestHeadRead.InitialBodyBytes, requestHead, listener, timeouts,
                route.ResolvedOptions.MaxRequestBodyBytes, preReadRequestBodyReader, preReadChunkLine, cancellationToken).ConfigureAwait(false);
            return await RelayResponseAsync(upstreamStream, clientStream, requestHead, route, listener, timeouts, preferClientKeepAlive,
                upstreamTarget, requestId, suppressGeneratedFailureResponse, markResponseStarted, cancellationToken).ConfigureAwait(false);
        }
        await exchange.AllowUploadAsync(cancellationToken).ConfigureAwait(false);
        return await ForwardDuplexHttp1Async(upstreamStream, clientStream, requestHeadRead, requestHead, route, listener, timeouts,
            upstreamTarget, preferClientKeepAlive, requestId, suppressGeneratedFailureResponse, preReadRequestBodyReader, preReadChunkLine,
            markResponseStarted, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ResponseForwardingResult> ForwardDuplexHttp1Async(Stream upstreamStream, Stream clientStream, Http1HeadReadResult requestHeadRead,
        Http1RequestHead requestHead, RuntimeRoute route, RuntimeListener listener, RuntimeTimeouts timeouts, string upstreamTarget,
        bool preferClientKeepAlive, string requestId, bool suppressGeneratedFailureResponse, Http1BodyReader? preReadRequestBodyReader,
        byte[]? preReadChunkLine, Action markResponseStarted, CancellationToken cancellationToken)
    {
        using var uploadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var responseCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var finalResponseReceived = false;
        async Task<bool> UploadAsync()
        {
            try
            {
                await RelayRequestBodyAsync(clientStream, upstreamStream, requestHeadRead.InitialBodyBytes, requestHead, listener, timeouts,
                    route.ResolvedOptions.MaxRequestBodyBytes, preReadRequestBodyReader, preReadChunkLine, uploadCancellation.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (finalResponseReceived && !cancellationToken.IsCancellationRequested) { return false; }
        }
        void OnFinalResponse(int statusCode)
        {
            if (statusCode < 300 && !string.Equals(requestHead.Method, "HEAD", StringComparison.OrdinalIgnoreCase)) return;
            finalResponseReceived = true;
            uploadCancellation.Cancel();
        }
        var upload = UploadAsync();
        var response = RelayResponseAsync(upstreamStream, clientStream, requestHead, route, listener, timeouts, preferClientKeepAlive,
            upstreamTarget, requestId, suppressGeneratedFailureResponse, markResponseStarted, responseCancellation.Token, OnFinalResponse).AsTask();
        try
        {
            var first = await Task.WhenAny(upload, response).ConfigureAwait(false);
            if (first == upload) await upload.ConfigureAwait(false);
            var result = await response.ConfigureAwait(false);
            if (clientStream is ExchangeClientStream exchange) await exchange.StopUploadAsync(cancellationToken).ConfigureAwait(false);
            finalResponseReceived = true;
            await uploadCancellation.CancelAsync().ConfigureAwait(false);
            var uploaded = await upload.ConfigureAwait(false);
            return result with { CanReuseUpstreamConnection = result.CanReuseUpstreamConnection && uploaded };
        }
        catch
        {
            await uploadCancellation.CancelAsync().ConfigureAwait(false);
            await responseCancellation.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(upload, response).ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ProxyTimeoutException) { }
            throw;
        }
    }

    private async ValueTask<ResponseForwardingResult> ForwardHttp2Async(Stream upstreamStream, Stream clientStream, Http1HeadReadResult requestHeadRead, Http1RequestHead requestHead, RuntimeRoute route, RuntimeUpstream upstream, RuntimeListener listener, RuntimeTimeouts timeouts, string upstreamTarget, ForwardedHeadersContext forwardedHeaders, bool preferClientKeepAlive, string requestId, bool suppressRetryableStatusResponse, Http1BodyReader? preReadRequestBodyReader, byte[]? preReadChunkLine, Action markResponseStarted, CancellationToken cancellationToken)
    {
        _metrics.UpstreamHttp2RequestAttempted();
        var upstreamHttp2 = new Http2UpstreamConnection(upstreamStream, _metrics, maxFrameSize: Math.Max(16 * 1024, listener.Http2Limits.MaxFrameSize));
        await using var upstreamHttp2Lifetime = upstreamHttp2.ConfigureAwait(false);
        await upstreamHttp2.InitializeAsync(timeouts, cancellationToken).ConfigureAwait(false);
        var requestHeaders = BuildHttp2RequestHeaders(requestHead, route, upstream, upstreamTarget, forwardedHeaders);
        var endRequestStream = !Http1RequestFramingPolicy.HasFramedBody(requestHead);
        await upstreamHttp2.SendHeadersAsync(requestHeaders, endRequestStream, timeouts, cancellationToken).ConfigureAwait(false);
        if (clientStream is ExchangeClientStream exchange) await exchange.AllowUploadAsync(cancellationToken).ConfigureAwait(false);
        Task UploadAsync(CancellationToken token) => endRequestStream ? Task.CompletedTask
            : RelayFramedUpstreamRequestBodyAsync(clientStream, requestHeadRead.InitialBodyBytes, requestHead, listener, timeouts,
                route.ResolvedOptions.MaxRequestBodyBytes, preReadRequestBodyReader, preReadChunkLine, upstreamHttp2.SendDataAsync,
                (fields, readTimeouts, cancellation) => upstreamHttp2.SendHeadersAsync(fields, true, readTimeouts, cancellation), token).AsTask();
        Task<ResponseForwardingResult> ResponseAsync(CancellationToken token, Action<int>? finalHead) =>
            ForwardHttp2ResponseAsync(upstreamHttp2, clientStream, requestHead, route, listener, timeouts, upstreamTarget,
                preferClientKeepAlive, requestId, suppressRetryableStatusResponse, markResponseStarted, finalHead, token).AsTask();
        if (clientStream is ExchangeClientStream duplex)
            return await ForwardFramedDuplexAsync(duplex, requestHead.Method, UploadAsync, ResponseAsync, cancellationToken).ConfigureAwait(false);
        await UploadAsync(cancellationToken).ConfigureAwait(false);
        return await ResponseAsync(cancellationToken, null).ConfigureAwait(false);
    }

    private async ValueTask<ResponseForwardingResult> ForwardHttp3Async(Stream clientStream, Http1HeadReadResult requestHeadRead, Http1RequestHead requestHead, RuntimeRoute route, RuntimeUpstream upstream, RuntimeListener listener, RuntimeTimeouts timeouts, RuntimeConnectionLimits connectionLimits, string upstreamTarget, ForwardedHeadersContext forwardedHeaders, bool preferClientKeepAlive, string requestId, bool suppressRetryableStatusResponse, Http1BodyReader? preReadRequestBodyReader, byte[]? preReadChunkLine, Action markResponseStarted, CancellationToken cancellationToken)
    {
        _metrics.UpstreamHttp3RequestAttempted();
        var upstreamHttp3 = (await _http3UpstreamConnections.BorrowAsync(upstream, timeouts, connectionLimits, Math.Max(16 * 1024, listener.Http2Limits.MaxFrameSize), cancellationToken).ConfigureAwait(false));
        await using var upstreamHttp3Disposal = upstreamHttp3.ConfigureAwait(false);
        var requestHeaders = BuildHttp2RequestHeaders(requestHead, route, upstream, upstreamTarget, forwardedHeaders);
        var endRequestStream = !Http1RequestFramingPolicy.HasFramedBody(requestHead);
        await upstreamHttp3.SendHeadersAsync(requestHeaders, endRequestStream, timeouts, cancellationToken).ConfigureAwait(false);
        if (clientStream is ExchangeClientStream exchange) await exchange.AllowUploadAsync(cancellationToken).ConfigureAwait(false);
        if (!endRequestStream)
        {
            await RelayFramedUpstreamRequestBodyAsync(clientStream, requestHeadRead.InitialBodyBytes, requestHead, listener, timeouts, route.ResolvedOptions.MaxRequestBodyBytes, preReadRequestBodyReader, preReadChunkLine, upstreamHttp3.SendDataAsync,
                (fields, readTimeouts, token) => upstreamHttp3.SendHeadersAsync(fields, true, readTimeouts, token), cancellationToken).ConfigureAwait(false);
        }

        return await ForwardHttp3ResponseAsync(upstreamHttp3, clientStream, requestHead, route, listener, timeouts,
            upstreamTarget, preferClientKeepAlive, requestId, suppressRetryableStatusResponse, markResponseStarted,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleTimeoutAsync(Stream clientStream, Http1RequestHead requestHead, RuntimeUpstream upstream, bool responseStarted, ProxyTimeoutException exception, RuntimeTimeouts timeouts, string requestId, CancellationToken cancellationToken, bool suppressGeneratedFailureResponse)
    {
        switch (exception.Kind)
        {
            case ProxyTimeoutKind.ClientRequestBodyIdle:
                _metrics.ClientRequestBodyTimedOut();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
                {
                    LogClientRequestBodyTimedOut10021(_logger, requestHead.Method, requestHead.Target, exception);
                }
                if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse))
                {
                    await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.ClientRequestBodyTimeout, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
                }

                break;
            case ProxyTimeoutKind.UpstreamConnect:
                _metrics.UpstreamConnectTimedOut();
                _metrics.UpstreamFailed();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
                {
                    LogTimedOutConnectingToUpstream10022(_logger, upstream.Name, exception);
                }
                if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse))
                {
                    await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.UpstreamConnectTimeout, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
                }

                break;
            case ProxyTimeoutKind.UpstreamResponseHead:
                _metrics.UpstreamResponseHeadTimedOut();
                _metrics.UpstreamFailed();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
                {
                    LogTimedOutWaitingForUpstream10023(_logger, upstream.Name, exception);
                }
                if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse))
                {
                    await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.UpstreamResponseHeadTimeout, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
                }

                break;
            case ProxyTimeoutKind.UpstreamResponseBodyIdle:
                _metrics.UpstreamResponseBodyTimedOut();
                _metrics.UpstreamFailed();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
                {
                    LogTimedOutRelayingUpstreamResponse10024(_logger, upstream.Name, exception);
                }
                break;
            case ProxyTimeoutKind.DownstreamWrite:
                _metrics.DownstreamWriteTimedOut();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
                {
                    LogDownstreamWriteTimedOutFor10025(_logger, requestHead.Method, requestHead.Target, exception);
                }
                break;
        }
    }

    private List<ProxyHeaderField> BuildHttp2RequestHeaders(Http1RequestHead requestHead, RuntimeRoute route, RuntimeUpstream upstream, string upstreamTarget, ForwardedHeadersContext forwardedHeaders)
    {
        var filtered = _headerPolicy.FilterForForwarding(requestHead.Headers, preserveTransferEncoding: false, preserveTrailer: false, preserveTeTrailers: true);
        var requestHeaders = ProxyHeaderMutationPolicy.ApplyRequestHeaders(filtered, ProxyHeaderMutationRuntimeMapper.ToPolicyInput(route.HeaderPolicy), forwardedHeaders);
        var authority = requestHeaders.FirstOrDefault(static header => string.Equals(header.Name, "Host", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(authority))
        {
            authority = requestHead.Host.Length == 0 ? upstream.Address : requestHead.Host;
        }

        List<ProxyHeaderField> headers = [new(":method", requestHead.Method), new(":scheme", upstream.Scheme), new(":authority", authority), new(":path", upstreamTarget)];
        foreach (var header in requestHeaders)
        {
            if (Http2HeaderPolicy.IsManagedUpstreamRequestHeader(header.Name))
            {
                continue;
            }

            // Serialize validated HTTP/2 field metadata with protocol-required lowercase names.
#pragma warning disable CA1308
            headers.Add(new ProxyHeaderField(header.Name.ToLowerInvariant(), header.Value));
#pragma warning restore CA1308
        }

        if (requestHead.Framing.Kind == Http1BodyKind.ContentLength)
        {
            headers.Add(new ProxyHeaderField("content-length", requestHead.Framing.ContentLength.GetValueOrDefault().ToString(CultureInfo.InvariantCulture)));
        }

        return headers;
    }

    private async ValueTask RelayFramedUpstreamRequestBodyAsync(Stream clientStream, ReadOnlyMemory<byte> initialBodyBytes, Http1RequestHead requestHead, RuntimeListener listener, RuntimeTimeouts timeouts, long maxRequestBodyBytes, Http1BodyReader? preReadReader, byte[]? preReadChunkLine, SendFramedUpstreamDataAsync sendDataAsync, SendFramedUpstreamTrailersAsync? sendTrailersAsync, CancellationToken cancellationToken)
    {
        var reader = preReadReader ?? new Http1BodyReader(clientStream, initialBodyBytes, _metrics, timeouts.ClientRequestBodyIdleTimeout, ProxyTimeoutKind.ClientRequestBodyIdle);
        try
        {
            if (requestHead.Framing.Kind == Http1BodyKind.ContentLength)
            {
                await RelayFixedLengthBodyToFramedUpstreamAsync(reader, sendDataAsync, requestHead.Framing.ContentLength.GetValueOrDefault(), listener.ForwardingBufferBytes, timeouts, cancellationToken).ConfigureAwait(false);
            }
            else if (requestHead.Framing.Kind == Http1BodyKind.Chunked)
            {
                await RelayChunkedBodyToFramedUpstreamAsync(reader, sendDataAsync, listener, timeouts, preReadChunkLine, maxRequestBodyBytes, sendTrailersAsync, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            _metrics.ClientBodyRelayFailed();
            throw;
        }
    }

    private static async ValueTask RelayFixedLengthBodyToFramedUpstreamAsync(Http1BodyReader reader, SendFramedUpstreamDataAsync sendDataAsync, long contentLength, int bufferSize, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var remaining = contentLength;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            while (remaining > 0)
            {
                var readLength = (int)Math.Min(buffer.Length, remaining);
                var bytesRead = await reader.ReadAsync(buffer.AsMemory(0, readLength), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    throw new IOException("Source closed before the declared Content-Length body was complete.");
                }

                remaining -= bytesRead;
                await sendDataAsync(buffer.AsMemory(0, bytesRead), remaining == 0, timeouts, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async ValueTask RelayChunkedBodyToFramedUpstreamAsync(Http1BodyReader reader, SendFramedUpstreamDataAsync sendDataAsync, RuntimeListener listener, RuntimeTimeouts timeouts, byte[]? initialChunkLine, long maxPayloadBytes, SendFramedUpstreamTrailersAsync? sendTrailersAsync, CancellationToken cancellationToken)
    {
        var chunkLine = initialChunkLine;
        var relayedPayloadBytes = 0L;
        var buffer = ArrayPool<byte>.Shared.Rent(listener.ForwardingBufferBytes);
        try
        {
            while (true)
            {
                chunkLine ??= await reader.ReadLineWithCrlfAsync(listener.MaxChunkLineBytes, cancellationToken).ConfigureAwait(false);
                if (!Http1ChunkSizeParser.TryParseLine(chunkLine.AsSpan(), out var chunkSize))
                {
                    throw new Http1ClientProtocolException("Invalid chunk-size line.");
                }

                if (chunkSize == 0)
                {
                    await FinishFramedRequestAsync(reader, sendDataAsync, sendTrailersAsync, listener.MaxChunkLineBytes,
                        timeouts, cancellationToken).ConfigureAwait(false);
                    return;
                }

                relayedPayloadBytes += chunkSize;
                if (relayedPayloadBytes > maxPayloadBytes)
                {
                    throw new Http1PayloadTooLargeException("Chunked request body exceeded the configured maximum request body size.");
                }

                var remaining = chunkSize;
                while (remaining > 0)
                {
                    var readLength = (int)Math.Min(buffer.Length, remaining);
                    var bytesRead = await reader.ReadAsync(buffer.AsMemory(0, readLength), cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        throw new IOException("Source closed before the declared chunk body was complete.");
                    }

                    remaining -= bytesRead;
                    await sendDataAsync(buffer.AsMemory(0, bytesRead), endStream: false, timeouts, cancellationToken).ConfigureAwait(false);
                }

                var crlf = await reader.ReadExactAsync(2, cancellationToken).ConfigureAwait(false);
                if (crlf.AsSpan()[0] != (byte)'\r' || crlf.AsSpan()[1] != (byte)'\n')
                {
                    throw new Http1ClientProtocolException("Chunk data was not followed by CRLF.");
                }

                chunkLine = null;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async ValueTask DiscardTrailerSectionAsync(Http1BodyReader reader, int maxLineBytes, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineWithCrlfAsync(maxLineBytes, cancellationToken).ConfigureAwait(false);
            if (line.Length == 2)
            {
                return;
            }

            var colon = line.AsSpan()[..^2].IndexOf((byte)':');
            if (colon <= 0)
            {
                throw new Http1ClientProtocolException("Invalid trailer field line.");
            }
        }
    }

    private static async ValueTask<BufferedFramedBody> ReadFramedUpstreamCacheCandidateBodyAsync(ReadFramedUpstreamDataAsync readDataAsync, Http1ResponseHead responseHead, bool endStream, long maximumBufferBytes, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBufferBytes);
        var length = new FramedUpstreamBodyLength(responseHead.Framing);
        if (endStream)
        {
            length.Observe(0, true);
            return new BufferedFramedBody([], null);
        }

        using var body = new MemoryStream();
        while (true)
        {
            var chunk = await readDataAsync(timeouts, cancellationToken).ConfigureAwait(false);
            length.Observe(chunk.Data.Length, chunk.EndStream);
            if (chunk.Data.Length > maximumBufferBytes - body.Length)
                throw new FramedUpstreamProtocolException("Upstream cache candidate exceeded its buffer bound.");
            if (chunk.Data.Length > 0) body.Write(chunk.Data.Span);

            if (chunk.EndStream)
            {
                return new BufferedFramedBody(body.ToArray(), chunk.Trailers);
            }
        }
    }

    private static async ValueTask<FramedUpstreamDataChunk> ReadHttp2DataChunkAsync(Http2UpstreamConnection upstreamHttp2, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var chunk = await upstreamHttp2.ReadDataAsync(timeouts, cancellationToken).ConfigureAwait(false);
        return new FramedUpstreamDataChunk(chunk.Data, chunk.EndStream, chunk.Trailers);
    }

    private static async ValueTask<FramedUpstreamDataChunk> ReadHttp3DataChunkAsync(Http3UpstreamConnection upstreamHttp3, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var chunk = await upstreamHttp3.ReadDataAsync(timeouts, cancellationToken).ConfigureAwait(false);
        return new FramedUpstreamDataChunk(chunk.Data, chunk.EndStream, chunk.Trailers);
    }

    private async ValueTask RelayHttp2ResponseBodyAsync(Http2UpstreamConnection upstreamHttp2, Stream clientStream, bool endStream, Http1ResponseHead responseHead, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        await RelayFramedUpstreamResponseBodyAsync((readTimeouts, token) => ReadHttp2DataChunkAsync(upstreamHttp2, readTimeouts, token), clientStream, responseHead, endStream, timeouts, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RelayHttp3ResponseBodyAsync(Http3UpstreamConnection upstreamHttp3, Stream clientStream, Http1ResponseHead responseHead, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        await RelayFramedUpstreamResponseBodyAsync((readTimeouts, token) => ReadHttp3DataChunkAsync(upstreamHttp3, readTimeouts, token), clientStream, responseHead, endStream: false, timeouts, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RelayFramedUpstreamResponseBodyAsync(ReadFramedUpstreamDataAsync readDataAsync, Stream clientStream, Http1ResponseHead responseHead, bool endStream, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var length = new FramedUpstreamBodyLength(responseHead.Framing);
        if (endStream)
        {
            length.Observe(0, true);
            return;
        }

        try
        {
            while (true)
            {
                var chunk = await readDataAsync(timeouts, cancellationToken).ConfigureAwait(false);
                length.Observe(chunk.Data.Length, chunk.EndStream);
                if (chunk.Data.Length > 0)
                {
                    if (responseHead.Framing.Kind == Http1BodyKind.Chunked)
                    {
                        await WriteHttp1ChunkAsync(clientStream, chunk.Data, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await ProxyTimedStreamWriter.WriteAsync(clientStream, chunk.Data, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
                        _metrics.AddBytesWritten(chunk.Data.Length);
                    }
                }

                if (chunk.EndStream)
                {
                    if (responseHead.Framing.Kind == Http1BodyKind.Chunked)
                    {
                        await WriteFramedResponseEndingAsync(clientStream, chunk.Trailers, timeouts, cancellationToken).ConfigureAwait(false);
                    }

                    if (clientStream is ExchangeClientStream exchange && chunk.Trailers is { Count: > 0 })
                        exchange.SetResponseTrailers(chunk.Trailers);
                    else if (responseHead.Framing.Kind != Http1BodyKind.Chunked && chunk.Trailers is { Count: > 0 })
                        throw new Http2UpstreamProtocolException("Fixed-length HTTP/1 presentation cannot carry trailing fields.");
                    return;
                }
            }
        }
        catch
        {
            _metrics.UpstreamBodyRelayFailed();
            _metrics.UpstreamPrematureDisconnect();
            throw;
        }
    }

    private async ValueTask WriteHttp1ChunkAsync(Stream clientStream, ReadOnlyMemory<byte> data, TimeSpan writeTimeout, CancellationToken cancellationToken)
    {
        var prefix = Encoding.ASCII.GetBytes(data.Length.ToString("x", CultureInfo.InvariantCulture) + "\r\n");
        await ProxyTimedStreamWriter.WriteAsync(clientStream, prefix, writeTimeout, cancellationToken).ConfigureAwait(false);
        await ProxyTimedStreamWriter.WriteAsync(clientStream, data, writeTimeout, cancellationToken).ConfigureAwait(false);
        await ProxyTimedStreamWriter.WriteAsync(clientStream, "\r\n"u8.ToArray(), writeTimeout, cancellationToken).ConfigureAwait(false);
        _metrics.AddBytesWritten(prefix.Length + data.Length + 2);
    }

    private async ValueTask WriteRequestHeadAsync(Stream upstreamStream, Http1RequestHead requestHead, RuntimeRoute route, string upstreamTarget, ForwardedHeadersContext forwardedHeaders, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.Append(requestHead.Method).Append(' ').Append(upstreamTarget).Append(' ').Append("HTTP/1.1").Append("\r\n");
        var filtered = _headerPolicy.FilterForForwarding(requestHead.Headers, preserveTransferEncoding: false, preserveTrailer: requestHead.Framing.Kind == Http1BodyKind.Chunked);
        var requestHeaders = ProxyHeaderMutationPolicy.ApplyRequestHeaders(filtered, ProxyHeaderMutationRuntimeMapper.ToPolicyInput(route.HeaderPolicy), forwardedHeaders);
        foreach (var header in requestHeaders)
        {
            if (Http1ManagedHeaderPolicy.IsManagedFramingHeader(header.Name))
            {
                continue;
            }

            builder.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        }

        if (requestHead.Framing.Kind == Http1BodyKind.ContentLength)
        {
            builder.Append("Content-Length: ").Append(requestHead.Framing.ContentLength.GetValueOrDefault()).Append("\r\n");
        }
        else if (requestHead.Framing.Kind == Http1BodyKind.Chunked)
        {
            builder.Append("Transfer-Encoding: chunked\r\n");
        }

        builder.Append("Connection: keep-alive\r\n\r\n");
        var bytes = Encoding.ASCII.GetBytes(builder.ToString());
        await ProxyTimedStreamWriter.WriteAsync(upstreamStream, bytes, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
        _metrics.AddBytesWritten(bytes.Length);
    }

    private async ValueTask RelayRequestBodyAsync(Stream clientStream, Stream upstreamStream, ReadOnlyMemory<byte> initialBodyBytes, Http1RequestHead requestHead, RuntimeListener listener, RuntimeTimeouts timeouts, long maxRequestBodyBytes, Http1BodyReader? preReadReader, byte[]? preReadChunkLine, CancellationToken cancellationToken)
    {
        var reader = preReadReader ?? new Http1BodyReader(clientStream, initialBodyBytes, _metrics, timeouts.ClientRequestBodyIdleTimeout, ProxyTimeoutKind.ClientRequestBodyIdle);
        try
        {
            if (requestHead.Framing.Kind == Http1BodyKind.ContentLength)
            {
                await RelayFixedLengthBodyAsync(reader, upstreamStream, requestHead.Framing.ContentLength.GetValueOrDefault(), listener.ForwardingBufferBytes, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
            }
            else if (requestHead.Framing.Kind == Http1BodyKind.Chunked)
            {
                await RelayChunkedBodyAsync(reader, upstreamStream, listener, timeouts.DownstreamWriteTimeout, preReadChunkLine, maxRequestBodyBytes, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            _metrics.ClientBodyRelayFailed();
            throw;
        }
    }

    private async ValueTask<ResponseForwardingResult> RelayResponseAsync(Stream upstreamStream, Stream clientStream, Http1RequestHead requestHead, RuntimeRoute route, RuntimeListener listener, RuntimeTimeouts timeouts, bool preferClientKeepAlive, string upstreamTarget, string requestId, bool suppressRetryableStatusResponse, Action markResponseStarted, CancellationToken cancellationToken, Action<int>? finalResponseReceived = null)
    {
        ReadOnlyMemory<byte> initialBodyBytes = ReadOnlyMemory<byte>.Empty;
        var responseStarted = false;
        var informationalCount = 0;
        Stream responseInput = upstreamStream;
        List<PrefixReadStream>? prefixes = null;
        try
        {
            while (true)
            {
                var responseHeadRead = await Http1UpstreamResponseHeadReader.ReadAsync(responseInput, listener.MaxResponseHeadBytes, timeouts.UpstreamResponseHeadTimeout, _metrics, cancellationToken).ConfigureAwait(false);
                var responseHead = ParseUpstreamResponseHead(responseHeadRead, requestHead.Method);
                var upstreamWantsClose = HopByHopHeaderPolicy.HasConnectionToken(responseHead.Headers, "close");
                var keepClientConnectionOpen = preferClientKeepAlive && responseHead.Framing.Kind != Http1BodyKind.CloseDelimited;
                initialBodyBytes = responseHeadRead.InitialBodyBytes;
                if (!Http1ResponseParser.IsInformational(responseHead))
                {
                    finalResponseReceived?.Invoke(responseHead.StatusCode);
                    if (ProxyRetryPolicy.ShouldSuppressRetryableStatusResponse(ProxyRetryRuntimeMapper.ToOutcomeInput(route.Retry), responseHead.StatusCode, suppressRetryableStatusResponse))
                    {
                        return CreateRetrySuppressedResult(responseHead.StatusCode);
                    }

                    var responseHeaders = BuildResponseHeaders(responseHead, route);
                    var bodyReader = new Http1BodyReader(responseInput, initialBodyBytes, _metrics, timeouts.UpstreamResponseBodyIdleTimeout, ProxyTimeoutKind.UpstreamResponseBodyIdle);
                    if (ProxyCacheEligibilityPolicy.EvaluateResponseForBuffering(ProxyCacheRuntimeMapper.ToPolicyFacts(route.Cache), requestHead, responseHead) is ProxyCacheEligibilityResult.AcceptedResult)
                    {
                        var body = await ReadCacheCandidateBodyAsync(bodyReader, responseHead, listener, cancellationToken).ConfigureAwait(false);
                        await WriteAndStoreBufferedCacheResponseAsync(clientStream, route, listener, timeouts, requestHead, upstreamTarget, responseHead, responseHeaders, body, keepClientConnectionOpen, requestId, () =>
                        {
                            responseStarted = true;
                            markResponseStarted();
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        RecordUncacheableFraming(route.Cache, responseHead);
                        await WriteResponseHeadAsync(clientStream, responseHead, responseHeaders, timeouts, keepClientConnectionOpen, requestId, listener, cancellationToken).ConfigureAwait(false);
                        responseStarted = true;
                        markResponseStarted();
                        await RelayResponseBodyAsync(bodyReader, clientStream, responseHead, listener, timeouts, cancellationToken).ConfigureAwait(false);
                    }

                    var canReuseUpstream = !upstreamWantsClose && responseHead.Framing.Kind != Http1BodyKind.CloseDelimited && !bodyReader.HasBufferedBytes;
                    return new ResponseForwardingResult(responseStarted, keepClientConnectionOpen, canReuseUpstream, responseHead.StatusCode);
                }

                if (++informationalCount > 8) throw new Http1UpstreamProtocolException("Too many informational responses.");
                var informationalHeaders = BuildResponseHeaders(responseHead, route);
                await WriteResponseHeadAsync(clientStream, responseHead, informationalHeaders, timeouts, keepClientConnectionOpen, requestId, listener, cancellationToken).ConfigureAwait(false);
                responseStarted = true;
                markResponseStarted();
                if (!initialBodyBytes.IsEmpty) responseInput = PrependResponseBytes(responseInput, initialBodyBytes, ref prefixes);
            }
        }
        finally
        {
            await DisposeResponsePrefixesAsync(prefixes).ConfigureAwait(false);
        }
    }

    private static Http1ResponseHead ParseUpstreamResponseHead(Http1HeadReadResult headRead, string requestMethod)
    {
        if (!headRead.HasReadableHead)
        {
            throw new Http1UpstreamProtocolException("Upstream closed before a complete response head was received.");
        }

        if (!Http1ResponseParser.TryParse(headRead.HeadBytes.Span, requestMethod, out var responseHead, out var error))
        {
            throw new Http1UpstreamProtocolException($"Upstream response head was invalid: {error}.");
        }

        return responseHead;
    }

    private static PrefixReadStream PrependResponseBytes(Stream input, ReadOnlyMemory<byte> bytes, ref List<PrefixReadStream>? prefixes)
    {
        var prefix = new PrefixReadStream(input, bytes);
        (prefixes ??= []).Add(prefix);
        return prefix;
    }

    private static async ValueTask DisposeResponsePrefixesAsync(List<PrefixReadStream>? prefixes)
    {
        if (prefixes is null) return;
        for (var index = prefixes.Count - 1; index >= 0; index--)
            await prefixes[index].DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask WriteResponseHeadAsync(Stream clientStream, Http1ResponseHead responseHead, IReadOnlyList<ProxyHeaderField> responseHeaders, RuntimeTimeouts timeouts, bool keepClientConnectionOpen, string requestId, RuntimeListener listener, CancellationToken cancellationToken)
    {
        await Http1ResponseHeadWriter.WriteAsync(clientStream, responseHead, responseHeaders, Http3AltSvcPolicy.ApplyHeader([], _altSvcPolicy.CreateHeader(ProxyHttp3AltSvcRuntimeMapper.ToListenerInput(listener))), requestId, ProxyResponseContentLengthPolicy.GetContentLength(responseHead), responseHead.Framing.Kind == Http1BodyKind.Chunked, keepClientConnectionOpen, timeouts.DownstreamWriteTimeout, _metrics, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteBufferedResponseAsync(Stream clientStream, Http1ResponseHead responseHead, IReadOnlyList<ProxyHeaderField> responseHeaders, byte[] body, bool keepClientConnectionOpen, string requestId, RuntimeListener listener, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        await Http1ResponseHeadWriter.WriteAsync(clientStream, responseHead, responseHeaders, Http3AltSvcPolicy.ApplyHeader([], _altSvcPolicy.CreateHeader(ProxyHttp3AltSvcRuntimeMapper.ToListenerInput(listener))), requestId, ProxyResponseContentLengthPolicy.GetContentLength(responseHead, body.LongLength), useChunkedTransferEncoding: false, keepClientConnectionOpen, timeouts.DownstreamWriteTimeout, _metrics, cancellationToken).ConfigureAwait(false);
        if (body.Length > 0)
        {
            await ProxyTimedStreamWriter.WriteAsync(clientStream, body, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
            _metrics.AddBytesWritten(body.Length);
        }
    }

    private async ValueTask WriteAndStoreBufferedCacheResponseAsync(Stream clientStream, RuntimeRoute route, RuntimeListener listener, RuntimeTimeouts timeouts, Http1RequestHead requestHead, string upstreamTarget, Http1ResponseHead responseHead, IReadOnlyList<ProxyHeaderField> responseHeaders, byte[] body, bool keepClientConnectionOpen, string requestId, Action markResponseStarted, CancellationToken cancellationToken)
    {
        await WriteBufferedResponseAsync(clientStream, responseHead, responseHeaders, body, keepClientConnectionOpen, requestId, listener, timeouts, cancellationToken).ConfigureAwait(false);
        markResponseStarted();
        _cacheStore.Store(ProxyCacheRuntimeMapper.ToRequestScope(route, listener), requestHead, upstreamTarget, responseHead, responseHeaders, body);
    }

    private IReadOnlyList<ProxyHeaderField> BuildResponseHeaders(Http1ResponseHead responseHead, RuntimeRoute route)
    {
        var filtered = _headerPolicy.FilterForForwarding(responseHead.Headers, preserveTransferEncoding: false, preserveTrailer: responseHead.Framing.Kind == Http1BodyKind.Chunked);
        return ProxyHeaderMutationPolicy.ApplyResponseHeaders(filtered, ProxyHeaderMutationRuntimeMapper.ToPolicyInput(route.HeaderPolicy));
    }

    private static ResponseForwardingResult CreateRetrySuppressedResult(int statusCode)
    {
        return new ResponseForwardingResult(false, false, false, statusCode, SuppressedForRetry: true);
    }

    private void RecordUncacheableFraming(RuntimeCachePolicy policy, Http1ResponseHead responseHead)
    {
        var policyFacts = ProxyCacheRuntimeMapper.ToPolicyFacts(policy);
        if (ProxyCacheEligibilityPolicy.EvaluateResponseFraming(policyFacts, responseHead)is ProxyCacheResponseFramingEligibility.Rejected rejected)
        {
            _cacheStore.RecordUncacheable(policyFacts, rejected.Reason);
        }
    }

    private static async ValueTask<byte[]> ReadCacheCandidateBodyAsync(Http1BodyReader reader, Http1ResponseHead responseHead, RuntimeListener listener, CancellationToken cancellationToken)
    {
        if (responseHead.Framing.Kind == Http1BodyKind.None)
        {
            return[];
        }

        var contentLength = responseHead.Framing.ContentLength.GetValueOrDefault();
        using var body = new MemoryStream((int)Math.Min(contentLength, int.MaxValue));
        var remaining = contentLength;
        var buffer = ArrayPool<byte>.Shared.Rent(listener.ForwardingBufferBytes);
        try
        {
            while (remaining > 0)
            {
                var readLength = (int)Math.Min(buffer.Length, remaining);
                var bytesRead = await reader.ReadAsync(buffer.AsMemory(0, readLength), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    throw new IOException("Source closed before the declared Content-Length body was complete.");
                }

                await body.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                remaining -= bytesRead;
            }

            return body.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async ValueTask RelayResponseBodyAsync(Http1BodyReader reader, Stream clientStream, Http1ResponseHead responseHead, RuntimeListener listener, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        try
        {
            if (responseHead.Framing.Kind == Http1BodyKind.ContentLength)
            {
                await RelayFixedLengthBodyAsync(reader, clientStream, responseHead.Framing.ContentLength.GetValueOrDefault(), listener.ForwardingBufferBytes, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
            }
            else if (responseHead.Framing.Kind == Http1BodyKind.Chunked)
            {
                await RelayChunkedBodyAsync(reader, clientStream, listener, timeouts.DownstreamWriteTimeout, null, null, cancellationToken).ConfigureAwait(false);
            }
            else if (responseHead.Framing.Kind == Http1BodyKind.CloseDelimited)
            {
                await RelayCloseDelimitedBodyAsync(reader, clientStream, listener.ForwardingBufferBytes, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            _metrics.UpstreamBodyRelayFailed();
            _metrics.UpstreamPrematureDisconnect();
            throw;
        }
    }

    private async ValueTask RelayFixedLengthBodyAsync(Http1BodyReader reader, Stream destination, long contentLength, int bufferSize, TimeSpan writeTimeout, CancellationToken cancellationToken)
    {
        var remaining = contentLength;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            while (remaining > 0)
            {
                var readLength = (int)Math.Min(buffer.Length, remaining);
                var bytesRead = await reader.ReadAsync(buffer.AsMemory(0, readLength), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    throw new IOException("Source closed before the declared Content-Length body was complete.");
                }

                await ProxyTimedStreamWriter.WriteAsync(destination, buffer.AsMemory(0, bytesRead), writeTimeout, cancellationToken).ConfigureAwait(false);
                _metrics.AddBytesWritten(bytesRead);
                remaining -= bytesRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async ValueTask RelayCloseDelimitedBodyAsync(Http1BodyReader reader, Stream destination, int bufferSize, TimeSpan writeTimeout, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            while (true)
            {
                var bytesRead = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                await ProxyTimedStreamWriter.WriteAsync(destination, buffer.AsMemory(0, bytesRead), writeTimeout, cancellationToken).ConfigureAwait(false);
                _metrics.AddBytesWritten(bytesRead);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async ValueTask RelayChunkedBodyAsync(Http1BodyReader reader, Stream destination, RuntimeListener listener, TimeSpan writeTimeout, byte[]? initialChunkLine, long? maxPayloadBytes, CancellationToken cancellationToken)
    {
        var chunkLine = initialChunkLine;
        var relayedPayloadBytes = 0L;
        while (true)
        {
            chunkLine ??= await reader.ReadLineWithCrlfAsync(listener.MaxChunkLineBytes, cancellationToken).ConfigureAwait(false);
            if (!Http1ChunkSizeParser.TryParseLine(chunkLine.AsSpan(), out var chunkSize))
            {
                throw new Http1ClientProtocolException("Invalid chunk-size line.");
            }

            await ProxyTimedStreamWriter.WriteAsync(destination, chunkLine, writeTimeout, cancellationToken).ConfigureAwait(false);
            _metrics.AddBytesWritten(chunkLine.Length);
            if (chunkSize == 0)
            {
                await RelayTrailerSectionAsync(reader, destination, listener.MaxChunkLineBytes, writeTimeout, cancellationToken).ConfigureAwait(false);
                return;
            }

            relayedPayloadBytes += chunkSize;
            if (maxPayloadBytes.HasValue && relayedPayloadBytes > maxPayloadBytes.Value)
            {
                throw new Http1PayloadTooLargeException("Chunked request body exceeded the configured maximum request body size.");
            }

            await RelayFixedLengthBodyAsync(reader, destination, chunkSize, listener.ForwardingBufferBytes, writeTimeout, cancellationToken).ConfigureAwait(false);
            var crlf = await reader.ReadExactAsync(2, cancellationToken).ConfigureAwait(false);
            if (crlf.AsSpan()[0] != (byte)'\r' || crlf.AsSpan()[1] != (byte)'\n')
            {
                throw new Http1ClientProtocolException("Chunk data was not followed by CRLF.");
            }

            await ProxyTimedStreamWriter.WriteAsync(destination, crlf, writeTimeout, cancellationToken).ConfigureAwait(false);
            _metrics.AddBytesWritten(crlf.Length);
            chunkLine = null;
        }
    }

    private async ValueTask RelayTrailerSectionAsync(Http1BodyReader reader, Stream destination, int maxLineBytes, TimeSpan writeTimeout, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineWithCrlfAsync(maxLineBytes, cancellationToken).ConfigureAwait(false);
            await ProxyTimedStreamWriter.WriteAsync(destination, line, writeTimeout, cancellationToken).ConfigureAwait(false);
            _metrics.AddBytesWritten(line.Length);
            if (line.Length == 2)
            {
                return;
            }

            var colon = line.AsSpan()[..^2].IndexOf((byte)':');
            if (colon <= 0)
            {
                throw new Http1ClientProtocolException("Invalid trailer field line.");
            }
        }
    }

    private sealed record ResponseForwardingResult(bool ResponseStarted, bool KeepClientConnectionOpen, bool CanReuseUpstreamConnection, int StatusCode, bool SuppressedForRetry = false);
    private sealed class Http1UpstreamProtocolException : IOException
    {
        public Http1UpstreamProtocolException(string message) : base(message)
        {
        }

        public Http1UpstreamProtocolException() : base()
        {
        }

        public Http1UpstreamProtocolException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        public Http1UpstreamProtocolException(string? message, int hresult) : base(message, hresult)
        {
        }
    }

    private sealed class Http1ClientProtocolException : IOException
    {
        public Http1ClientProtocolException(string message) : base(message)
        {
        }

        public Http1ClientProtocolException() : base()
        {
        }

        public Http1ClientProtocolException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        public Http1ClientProtocolException(string? message, int hresult) : base(message, hresult)
        {
        }
    }

    private sealed class Http1PayloadTooLargeException : IOException
    {
        public Http1PayloadTooLargeException(string message) : base(message)
        {
        }

        public Http1PayloadTooLargeException() : base()
        {
        }

        public Http1PayloadTooLargeException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        public Http1PayloadTooLargeException(string? message, int hresult) : base(message, hresult)
        {
        }
    }

    private delegate ValueTask SendFramedUpstreamDataAsync(ReadOnlyMemory<byte> data, bool endStream, RuntimeTimeouts timeouts, CancellationToken cancellationToken);
    private delegate ValueTask<FramedUpstreamDataChunk> ReadFramedUpstreamDataAsync(RuntimeTimeouts timeouts, CancellationToken cancellationToken);
    private delegate ValueTask SendFramedUpstreamTrailersAsync(IReadOnlyList<ProxyHeaderField> fields, RuntimeTimeouts timeouts, CancellationToken cancellationToken);
    private readonly record struct FramedUpstreamDataChunk(ReadOnlyMemory<byte> Data, bool EndStream, IReadOnlyList<ProxyHeaderField>? Trailers = null);
    private sealed record BufferedFramedBody(byte[] Data, IReadOnlyList<ProxyHeaderField>? Trailers);
}
