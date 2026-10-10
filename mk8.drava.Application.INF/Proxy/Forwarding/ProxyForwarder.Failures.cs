using System.Net.Sockets;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Mk8.Drava.Application.INF.Proxy.Http3;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;

public sealed partial class ProxyForwarder
{
    private static bool IsExpectedForwardingFailure(Exception exception)
    {
        return exception is ProxyTimeoutException or Http1PayloadTooLargeException or Http1ClientProtocolException or Http1UpstreamProtocolException or FramedUpstreamProtocolException or Http2UpstreamProtocolException or Http3UpstreamProtocolException or UpstreamTlsException or SocketException or IOException;
    }

    private ValueTask<ForwardingResult> HandleForwardingFailureAsync(ForwardingFailureContext context, Exception exception, CancellationToken cancellationToken)
    {
        return exception switch
        {
            ProxyTimeoutException timeout => HandleForwardingTimeoutAsync(context, timeout, cancellationToken),
            Http1PayloadTooLargeException oversized => HandleOversizedRequestBodyAsync(context, oversized, cancellationToken),
            Http1ClientProtocolException malformedRequest => HandleMalformedRequestBodyAsync(context, malformedRequest, cancellationToken),
            Http1UpstreamProtocolException or FramedUpstreamProtocolException => HandleMalformedUpstreamResponseAsync(context, exception, cancellationToken),
            Http2UpstreamProtocolException http2 => HandleHttp2ProtocolFailureAsync(context, http2, cancellationToken),
            Http3UpstreamProtocolException http3 => HandleHttp3ProtocolFailureAsync(context, http3, cancellationToken),
            UpstreamTlsException tls => HandleUpstreamTlsFailureAsync(context, tls, cancellationToken),
            SocketException or IOException => HandleUpstreamTransportFailureAsync(context, exception, cancellationToken),
            _ => ValueTask.FromException<ForwardingResult>(exception),
        };
    }

    private async ValueTask<ForwardingResult> HandleForwardingTimeoutAsync(ForwardingFailureContext context, ProxyTimeoutException exception, CancellationToken cancellationToken)
    {
        var timeoutFailure = ProxyTimeoutFailurePolicy.ClassifyForwardingTimeout(exception.Kind, context.ResponseStarted);
        await HandleTimeoutAsync(context, exception, cancellationToken).ConfigureAwait(false);
        return ForwardingResult.Failure(context.ResponseStarted, timeoutFailure.ResponseStatusCode, timeoutFailure.FailureKind);
    }

    private async ValueTask<ForwardingResult> HandleOversizedRequestBodyAsync(ForwardingFailureContext context, Http1PayloadTooLargeException exception, CancellationToken cancellationToken)
    {
        _metrics.RequestBodySizeRejected();
        _metrics.ClientBodyRelayFailed();
        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
        {
            LogRejectedOversizedRequestBodyFor10014(_logger, context.Method, context.Target, exception);
        }
        if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(context.ResponseStarted, context.SuppressGeneratedFailureResponse))
        {
            await ProxyGeneratedFailureWriter.WriteAsync(context.ClientStream, ProxyFailureKind.RequestPayloadTooLarge, context.Timeouts, context.RequestId, _metrics, cancellationToken).ConfigureAwait(false);
        }

        return ForwardingResult.Failure(context.ResponseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(context.ResponseStarted, ProxyFailureKind.RequestPayloadTooLarge), ProxyFailureKind.RequestPayloadTooLarge);
    }

    private async ValueTask<ForwardingResult> HandleMalformedRequestBodyAsync(ForwardingFailureContext context, Http1ClientProtocolException exception, CancellationToken cancellationToken)
    {
        _metrics.MalformedRequestRejected();
        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
        {
            LogRejectedMalformedRequestBodyFor10015(_logger, context.Method, context.Target, exception);
        }
        if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(context.ResponseStarted, context.SuppressGeneratedFailureResponse))
        {
            await ProxyGeneratedFailureWriter.WriteAsync(context.ClientStream, ProxyFailureKind.ClientMalformedRequest, context.Timeouts, context.RequestId, _metrics, cancellationToken).ConfigureAwait(false);
        }

        return ForwardingResult.Failure(context.ResponseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(context.ResponseStarted, ProxyFailureKind.ClientMalformedRequest), ProxyFailureKind.ClientMalformedRequest);
    }

    private async ValueTask<ForwardingResult> HandleMalformedUpstreamResponseAsync(ForwardingFailureContext context, Exception exception, CancellationToken cancellationToken)
    {
        _metrics.UpstreamMalformedResponse();
        _metrics.UpstreamFailed();
        if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(context.ResponseStarted, context.SuppressGeneratedFailureResponse))
        {
            _metrics.UpstreamConnectFailed();
        }

        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
        {
            LogUpstreamResponseFramingFailedFor10016(_logger, context.Method, context.Target, context.UpstreamName, exception);
        }
        if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(context.ResponseStarted, context.SuppressGeneratedFailureResponse))
        {
            await ProxyGeneratedFailureWriter.WriteAsync(context.ClientStream, ProxyFailureKind.UpstreamMalformedResponse, context.Timeouts, context.RequestId, _metrics, cancellationToken).ConfigureAwait(false);
        }

        return ForwardingResult.Failure(context.ResponseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(context.ResponseStarted, ProxyFailureKind.UpstreamMalformedResponse), ProxyFailureKind.UpstreamMalformedResponse);
    }

    private async ValueTask<ForwardingResult> HandleHttp2ProtocolFailureAsync(ForwardingFailureContext context, Http2UpstreamProtocolException exception, CancellationToken cancellationToken)
    {
        _metrics.UpstreamHttp2ProtocolError();
        _metrics.UpstreamMalformedResponse();
        _metrics.UpstreamFailed();
        if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(context.ResponseStarted, context.SuppressGeneratedFailureResponse))
        {
            _metrics.UpstreamConnectFailed();
        }

        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
        {
            LogUpstreamHTTPResponseFramingFailed10017(_logger, context.Method, context.Target, context.UpstreamName, exception);
        }
        if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(context.ResponseStarted, context.SuppressGeneratedFailureResponse))
        {
            await ProxyGeneratedFailureWriter.WriteAsync(context.ClientStream, ProxyFailureKind.UpstreamMalformedResponse, context.Timeouts, context.RequestId, _metrics, cancellationToken).ConfigureAwait(false);
        }

        return ForwardingResult.Failure(context.ResponseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(context.ResponseStarted, ProxyFailureKind.UpstreamMalformedResponse), ProxyFailureKind.UpstreamMalformedResponse);
    }

    private async ValueTask<ForwardingResult> HandleHttp3ProtocolFailureAsync(ForwardingFailureContext context, Http3UpstreamProtocolException exception, CancellationToken cancellationToken)
    {
        _metrics.UpstreamHttp3ProtocolError(exception.FailureKind == Http3UpstreamFailureKind.ConnectFailure ? "connect_failure" : "protocol_failure");
        _metrics.UpstreamFailed();
        var failureKind = ProxyForwardingFailurePolicy.ClassifyDisconnectingProtocolFailure(exception.FailureKind == Http3UpstreamFailureKind.ConnectFailure, context.ResponseStarted);
        if (failureKind == ProxyFailureKind.UpstreamMalformedResponse)
        {
            _metrics.UpstreamMalformedResponse();
        }

        if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(context.ResponseStarted, context.SuppressGeneratedFailureResponse))
        {
            _metrics.UpstreamConnectFailed();
        }

        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
        {
            LogUpstreamHTTPForwardingFailedFor10018(_logger, context.Method, context.Target, context.UpstreamName, exception);
        }
        if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(context.ResponseStarted, context.SuppressGeneratedFailureResponse))
        {
            await ProxyGeneratedFailureWriter.WriteAsync(context.ClientStream, failureKind, context.Timeouts, context.RequestId, _metrics, cancellationToken).ConfigureAwait(false);
        }

        return ForwardingResult.Failure(context.ResponseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(context.ResponseStarted, failureKind), failureKind);
    }

    private async ValueTask<ForwardingResult> HandleUpstreamTlsFailureAsync(ForwardingFailureContext context, UpstreamTlsException exception, CancellationToken cancellationToken)
    {
        _metrics.UpstreamFailed();
        if (RuntimeUpstreamProtocol.IsHttp2(context.UpstreamProtocol) && exception.Message.Contains("ALPN", StringComparison.OrdinalIgnoreCase))
        {
            _metrics.UpstreamHttp2AlpnFailed();
        }

        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
        {
            LogUpstreamTLSFailedForTo10019(_logger, context.Method, context.Target, context.UpstreamName, exception);
        }
        if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(context.ResponseStarted, context.SuppressGeneratedFailureResponse))
        {
            await ProxyGeneratedFailureWriter.WriteAsync(context.ClientStream, ProxyFailureKind.UpstreamConnectFailed, context.Timeouts, context.RequestId, _metrics, cancellationToken).ConfigureAwait(false);
        }

        var failureKind = ProxyForwardingFailurePolicy.ClassifyConnectionFailure(context.ResponseStarted);
        return ForwardingResult.Failure(context.ResponseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(context.ResponseStarted, failureKind), failureKind);
    }

    private async ValueTask<ForwardingResult> HandleUpstreamTransportFailureAsync(ForwardingFailureContext context, Exception exception, CancellationToken cancellationToken)
    {
        _metrics.UpstreamFailed();
        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
        {
            LogUpstreamForwardingFailedForTo10020(_logger, context.Method, context.Target, context.UpstreamName, exception);
        }
        if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(context.ResponseStarted, context.SuppressGeneratedFailureResponse))
        {
            await ProxyGeneratedFailureWriter.WriteAsync(context.ClientStream, ProxyFailureKind.UpstreamConnectFailed, context.Timeouts, context.RequestId, _metrics, cancellationToken).ConfigureAwait(false);
        }

        var failureKind = ProxyForwardingFailurePolicy.ClassifyConnectionFailure(context.ResponseStarted);
        return ForwardingResult.Failure(context.ResponseStarted, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(context.ResponseStarted, failureKind), failureKind);
    }

    private async ValueTask HandleTimeoutAsync(ForwardingFailureContext context, ProxyTimeoutException exception, CancellationToken cancellationToken)
    {
        switch (exception.Kind)
        {
            case ProxyTimeoutKind.ClientRequestBodyIdle:
                _metrics.ClientRequestBodyTimedOut();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
                {
                    LogClientRequestBodyTimedOut10021(_logger, context.Method, context.Target, exception);
                }
                if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(context.ResponseStarted, context.SuppressGeneratedFailureResponse))
                {
                    await ProxyGeneratedFailureWriter.WriteAsync(context.ClientStream, ProxyFailureKind.ClientRequestBodyTimeout, context.Timeouts, context.RequestId, _metrics, cancellationToken).ConfigureAwait(false);
                }

                break;
            case ProxyTimeoutKind.UpstreamConnect:
                _metrics.UpstreamConnectTimedOut();
                _metrics.UpstreamFailed();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
                {
                    LogTimedOutConnectingToUpstream10022(_logger, context.UpstreamName, exception);
                }
                if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(context.ResponseStarted, context.SuppressGeneratedFailureResponse))
                {
                    await ProxyGeneratedFailureWriter.WriteAsync(context.ClientStream, ProxyFailureKind.UpstreamConnectTimeout, context.Timeouts, context.RequestId, _metrics, cancellationToken).ConfigureAwait(false);
                }

                break;
            case ProxyTimeoutKind.UpstreamResponseHead:
                _metrics.UpstreamResponseHeadTimedOut();
                _metrics.UpstreamFailed();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
                {
                    LogTimedOutWaitingForUpstream10023(_logger, context.UpstreamName, exception);
                }
                if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(context.ResponseStarted, context.SuppressGeneratedFailureResponse))
                {
                    await ProxyGeneratedFailureWriter.WriteAsync(context.ClientStream, ProxyFailureKind.UpstreamResponseHeadTimeout, context.Timeouts, context.RequestId, _metrics, cancellationToken).ConfigureAwait(false);
                }

                break;
            case ProxyTimeoutKind.UpstreamResponseBodyIdle:
                _metrics.UpstreamResponseBodyTimedOut();
                _metrics.UpstreamFailed();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
                {
                    LogTimedOutRelayingUpstreamResponse10024(_logger, context.UpstreamName, exception);
                }
                break;
            case ProxyTimeoutKind.DownstreamWrite:
                _metrics.DownstreamWriteTimedOut();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
                {
                    LogDownstreamWriteTimedOutFor10025(_logger, context.Method, context.Target, exception);
                }
                break;
        }
    }

    // Borrowed I/O and request facts for one awaited failure response; ForwardAsync retains lease ownership.
    private readonly record struct ForwardingFailureContext(Stream ClientStream, string Method, string Target, string UpstreamName,
        string UpstreamProtocol, RuntimeTimeouts Timeouts, string RequestId, bool ResponseStarted, bool SuppressGeneratedFailureResponse);
}
