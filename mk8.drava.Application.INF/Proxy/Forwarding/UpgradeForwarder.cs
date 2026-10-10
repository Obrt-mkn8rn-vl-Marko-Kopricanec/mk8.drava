using Mk8.Drava.Application.INF.Proxy.Streams;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.ControlPlane.Upgrades;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.Application.INF.Proxy.Http1;
using Mk8.Drava.Application.DAL.Observability;
using Mk8.Drava.Application.INF.Observability;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;
public sealed partial class UpgradeForwarder
{
    private const string WebSocketAcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private readonly UpstreamConnectionFactory _connectionFactory;
    private readonly HopByHopHeaderPolicy _headerPolicy;
    private readonly TunnelRelay _tunnelRelay;
    private readonly ProxyMetrics _metrics;
    private readonly ILogger<UpgradeForwarder> _logger;
    public UpgradeForwarder(UpstreamConnectionFactory connectionFactory, HopByHopHeaderPolicy headerPolicy, TunnelRelay tunnelRelay, ProxyMetrics metrics, ILogger<UpgradeForwarder> logger)
    {
        _connectionFactory = connectionFactory;
        _headerPolicy = headerPolicy;
        _tunnelRelay = tunnelRelay;
        _metrics = metrics;
        _logger = logger;
    }

    public async ValueTask<ForwardingResult> ForwardAsync(Stream clientStream, Http1RequestHead requestHead, UpgradeRequestInfo upgrade, RuntimeRoute route, RuntimeUpstream upstream, RuntimeListener listener, RuntimeTimeouts timeouts, RuntimeConnectionLimits connectionLimits, string upstreamTarget, ForwardedHeadersContext forwardedHeaders, string requestId, CancellationToken cancellationToken)
    {
        RequireForwardingInputs(connectionLimits, upstream, requestHead, timeouts, route, listener, upgrade);
        var responseStarted = false;
        void MarkResponseStarted() => responseStarted = true;
        UpstreamTransportConnection? upstreamConnection = null;
        try
        {
            if (_metrics.StartTunnel(connectionLimits.MaxActiveUpgradedTunnels) is ProxyTunnelAdmissionDecision.RejectedResult)
                return await RejectAdmissionAsync(clientStream, timeouts, requestId, cancellationToken).ConfigureAwait(false);

            try
            {
                upstreamConnection = await _connectionFactory.ConnectAsync(UpstreamTransportEndpointMapper.FromUpstream(upstream), timeouts.UpstreamConnectTimeout, cancellationToken).ConfigureAwait(false);
                var upstreamStream = upstreamConnection.Stream;
                await WriteUpgradeRequestAsync(upstreamStream, requestHead, upgrade, route, upstreamTarget, forwardedHeaders, timeouts, cancellationToken).ConfigureAwait(false);
                var (responseHead, initialBody) = await ReadUpgradeResponseAsync(upstreamStream, requestHead, listener, timeouts, cancellationToken).ConfigureAwait(false);
                if (responseHead.StatusCode != 101)
                {
                    await ForwardNonUpgradeResponseAsync(upstreamStream, clientStream, initialBody, responseHead, route, listener, timeouts,
                        requestId, MarkResponseStarted, cancellationToken).ConfigureAwait(false);
                    return ForwardingResult.Success(responseStarted, keepClientConnectionOpen: false, responseHead.StatusCode);
                }

                if (!IsValidSwitchingProtocolsResponse(responseHead, upgrade))
                {
                    throw new Http1UpstreamProtocolException("Upstream returned an invalid 101 Switching Protocols response.");
                }

                await WriteSwitchingProtocolsResponseAsync(clientStream, responseHead, upgrade, route, timeouts, requestId,
                    MarkResponseStarted, cancellationToken).ConfigureAwait(false);
                return await RelayUpgradedAsync(clientStream, upstreamStream, initialBody, requestHead, upgrade, upstream,
                    listener, timeouts, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _metrics.TunnelClosed();
            }
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProxyTimeoutException exception)
        {
            return await HandleTimeoutAsync(clientStream, requestHead, upstream, responseStarted, exception, timeouts, requestId, cancellationToken).ConfigureAwait(false);
        }
        catch (Http1UpstreamProtocolException exception)
        {
            return await HandleMalformedUpgradeAsync(clientStream, requestHead, upstream, responseStarted, exception,
                timeouts, requestId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)when (exception is SocketException or IOException)
        {
            return await HandleUpgradeTransportFailureAsync(clientStream, requestHead, upstream, responseStarted, exception,
                timeouts, requestId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            upstreamConnection?.Dispose();
        }
    }

    private static void RequireForwardingInputs(RuntimeConnectionLimits connectionLimits, RuntimeUpstream upstream, Http1RequestHead requestHead,
        RuntimeTimeouts timeouts, RuntimeRoute route, RuntimeListener listener, UpgradeRequestInfo upgrade)
    {
        ArgumentNullException.ThrowIfNull(connectionLimits);
        ArgumentNullException.ThrowIfNull(upstream);
        ArgumentNullException.ThrowIfNull(requestHead);
        ArgumentNullException.ThrowIfNull(timeouts);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(upgrade);
    }

    private async ValueTask<ForwardingResult> RejectAdmissionAsync(Stream clientStream, RuntimeTimeouts timeouts, string requestId, CancellationToken cancellationToken)
    {
        _metrics.UpgradeRequestRejected();
        await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.UpgradeRejected, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
        return ForwardingResult.Failure(responseStarted: false,
            ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted: false, ProxyFailureKind.UpgradeRejected), ProxyFailureKind.UpgradeRejected);
    }

    private async ValueTask<(Http1ResponseHead Head, ReadOnlyMemory<byte> InitialBody)> ReadUpgradeResponseAsync(Stream upstreamStream,
        Http1RequestHead requestHead, RuntimeListener listener, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var read = await Http1UpstreamResponseHeadReader.ReadAsync(upstreamStream, listener.MaxResponseHeadBytes,
            timeouts.UpstreamResponseHeadTimeout, _metrics, cancellationToken).ConfigureAwait(false);
        if (!read.HasReadableHead) throw new Http1UpstreamProtocolException("Upstream closed before sending an Upgrade response.");
        if (!Http1ResponseParser.TryParse(read.HeadBytes.Span, requestHead.Method, out var head, out var error))
            throw new Http1UpstreamProtocolException($"Upstream Upgrade response was invalid: {error}.");
        return (head, read.InitialBodyBytes);
    }

    private async ValueTask<ForwardingResult> RelayUpgradedAsync(Stream clientStream, Stream upstreamStream, ReadOnlyMemory<byte> initialBody,
        Http1RequestHead requestHead, UpgradeRequestInfo upgrade, RuntimeUpstream upstream, RuntimeListener listener,
        RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        _metrics.UpgradeRequestSucceeded();
        _metrics.TunnelStarted();
        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            LogUpgradedToProtocolThroughUpstream10028(_logger, requestHead.Method, requestHead.Target, upgrade.Protocol, upstream.Name, null);
        using var input = new PrefixReadStream(upstreamStream, initialBody);
        var result = await _tunnelRelay.RelayAsync(clientStream, input, listener, timeouts, cancellationToken).ConfigureAwait(false);
        return ForwardingResult.TunnelCompleted(responseStatusCode: 101, tunnel: result);
    }

    private async ValueTask<ForwardingResult> HandleMalformedUpgradeAsync(Stream clientStream, Http1RequestHead requestHead,
        RuntimeUpstream upstream, bool responseStarted, Http1UpstreamProtocolException exception, RuntimeTimeouts timeouts,
        string requestId, CancellationToken cancellationToken)
    {
        _metrics.UpstreamMalformedResponse();
        _metrics.UpgradeUpstreamFailed();
        _metrics.UpstreamFailed();
        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
            LogUpstreamUpgradeResponseFailedFor10029(_logger, requestHead.Method, requestHead.Target, upstream.Name, exception);
        if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse: false))
            await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.UpstreamMalformedResponse, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
        return ForwardingResult.Failure(responseStarted,
            ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted, ProxyFailureKind.UpstreamMalformedResponse), ProxyFailureKind.UpstreamMalformedResponse);
    }

    private async ValueTask<ForwardingResult> HandleUpgradeTransportFailureAsync(Stream clientStream, Http1RequestHead requestHead,
        RuntimeUpstream upstream, bool responseStarted, Exception exception, RuntimeTimeouts timeouts,
        string requestId, CancellationToken cancellationToken)
    {
        _metrics.UpgradeUpstreamFailed();
        _metrics.UpstreamFailed();
        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
            LogUpgradeForwardingFailedForTo10030(_logger, requestHead.Method, requestHead.Target, upstream.Name, exception);
        if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse: false))
            await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.UpstreamConnectFailed, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
        return ForwardingResult.Failure(responseStarted,
            ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted, ProxyFailureKind.UpstreamConnectFailed), ProxyFailureKind.UpstreamConnectFailed);
    }

    private async ValueTask<ForwardingResult> HandleTimeoutAsync(Stream clientStream, Http1RequestHead requestHead, RuntimeUpstream upstream, bool responseStarted, ProxyTimeoutException exception, RuntimeTimeouts timeouts, string requestId, CancellationToken cancellationToken)
    {
        var failure = ProxyTimeoutFailurePolicy.ClassifyForwardingTimeout(exception.Kind, responseStarted);
        switch (exception.Kind)
        {
            case ProxyTimeoutKind.UpstreamConnect:
                _metrics.UpstreamConnectTimedOut();
                _metrics.UpgradeUpstreamFailed();
                _metrics.UpstreamFailed();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
                {
                    LogTimedOutConnectingUpgradeRequest10031(_logger, upstream.Name, exception);
                }
                if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse: false))
                {
                    await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.UpstreamConnectTimeout, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
                }

                break;
            case ProxyTimeoutKind.UpstreamResponseHead:
                _metrics.UpstreamResponseHeadTimedOut();
                _metrics.UpgradeUpstreamFailed();
                _metrics.UpstreamFailed();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
                {
                    LogTimedOutWaitingForUpstream10032(_logger, upstream.Name, exception);
                }
                if (ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted, suppressGeneratedFailureResponse: false))
                {
                    await ProxyGeneratedFailureWriter.WriteAsync(clientStream, ProxyFailureKind.UpstreamResponseHeadTimeout, timeouts, requestId, _metrics, cancellationToken).ConfigureAwait(false);
                }

                break;
            case ProxyTimeoutKind.UpstreamResponseBodyIdle:
                _metrics.UpstreamResponseBodyTimedOut();
                _metrics.UpstreamFailed();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
                {
                    LogTimedOutRelayingNonUpstream10033(_logger, upstream.Name, exception);
                }
                break;
            case ProxyTimeoutKind.DownstreamWrite:
                _metrics.DownstreamWriteTimedOut();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
                {
                    LogDownstreamWriteTimedOutFor10034(_logger, requestHead.Method, requestHead.Target, exception);
                }
                break;
        }
        return ForwardingResult.Failure(responseStarted, failure.ResponseStatusCode, failure.FailureKind);
    }

    private async ValueTask WriteUpgradeRequestAsync(Stream upstreamStream, Http1RequestHead requestHead, UpgradeRequestInfo upgrade, RuntimeRoute route, string upstreamTarget, ForwardedHeadersContext forwardedHeaders, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.Append(requestHead.Method).Append(' ').Append(upstreamTarget).Append(' ').Append("HTTP/1.1").Append("\r\n");
        var filtered = _headerPolicy.FilterForForwarding(requestHead.Headers, preserveTransferEncoding: false, preserveTrailer: false);
        var requestHeaders = ProxyHeaderMutationPolicy.ApplyRequestHeaders(filtered, ProxyHeaderMutationRuntimeMapper.ToPolicyInput(route.HeaderPolicy), forwardedHeaders);
        foreach (var header in requestHeaders)
        {
            if (UpgradeRequestPolicy.IsManagedUpgradeHeader(header.Name))
            {
                continue;
            }

            builder.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        }

        builder.Append("Upgrade: ").Append(upgrade.Protocol).Append("\r\n");
        builder.Append("Connection: Upgrade\r\n\r\n");
        var bytes = Encoding.ASCII.GetBytes(builder.ToString());
        await ProxyTimedStreamWriter.WriteAsync(upstreamStream, bytes, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
        _metrics.AddBytesWritten(bytes.Length);
    }

    private async ValueTask WriteSwitchingProtocolsResponseAsync(Stream clientStream, Http1ResponseHead responseHead, UpgradeRequestInfo upgrade, RuntimeRoute route,
        RuntimeTimeouts timeouts, string requestId, Action markResponseStarted, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.Append(responseHead.Version).Append(' ').Append(responseHead.StatusCode).Append(' ').Append(responseHead.ReasonPhrase).Append("\r\n");
        var responseHeaders = ProxyHeaderMutationPolicy.ApplyResponseHeaders(responseHead.Headers, ProxyHeaderMutationRuntimeMapper.ToPolicyInput(route.HeaderPolicy));
        foreach (var header in responseHeaders)
        {
            if (UpgradeRequestPolicy.IsManagedUpgradeHeader(header.Name) || UpgradeRequestPolicy.IsUnsafeSwitchingProtocolsResponseHeader(header.Name))
            {
                continue;
            }

            builder.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        }

        builder.Append("X-Request-Id: ").Append(requestId).Append("\r\n");
        builder.Append("Upgrade: ").Append(upgrade.Protocol).Append("\r\n");
        builder.Append("Connection: Upgrade\r\n\r\n");
        var bytes = Encoding.ASCII.GetBytes(builder.ToString());
        markResponseStarted();
        await ProxyTimedStreamWriter.WriteAsync(clientStream, bytes, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
        _metrics.AddBytesWritten(bytes.Length);
    }

    private async ValueTask ForwardNonUpgradeResponseAsync(Stream upstreamStream, Stream clientStream, ReadOnlyMemory<byte> initialBodyBytes, Http1ResponseHead responseHead,
        RuntimeRoute route, RuntimeListener listener, RuntimeTimeouts timeouts, string requestId, Action markResponseStarted, CancellationToken cancellationToken)
    {
        await WriteNonUpgradeResponseHeadAsync(clientStream, responseHead, route, timeouts, requestId, markResponseStarted, cancellationToken).ConfigureAwait(false);
        await RelayNonUpgradeResponseBodyAsync(upstreamStream, clientStream, initialBodyBytes, responseHead, listener, timeouts, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteNonUpgradeResponseHeadAsync(Stream clientStream, Http1ResponseHead responseHead, RuntimeRoute route, RuntimeTimeouts timeouts,
        string requestId, Action markResponseStarted, CancellationToken cancellationToken)
    {
        var filtered = _headerPolicy.FilterForForwarding(responseHead.Headers, preserveTransferEncoding: false, preserveTrailer: responseHead.Framing.Kind == Http1BodyKind.Chunked);
        var responseHeaders = ProxyHeaderMutationPolicy.ApplyResponseHeaders(filtered, ProxyHeaderMutationRuntimeMapper.ToPolicyInput(route.HeaderPolicy));
        markResponseStarted();
        await Http1ResponseHeadWriter.WriteAsync(clientStream, responseHead, responseHeaders, [], requestId, responseHead.Framing.Kind == Http1BodyKind.ContentLength ? responseHead.Framing.ContentLength : null, responseHead.Framing.Kind == Http1BodyKind.Chunked, keepClientConnectionOpen: false, timeouts.DownstreamWriteTimeout, _metrics, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RelayNonUpgradeResponseBodyAsync(Stream upstreamStream, Stream clientStream, ReadOnlyMemory<byte> initialBodyBytes, Http1ResponseHead responseHead, RuntimeListener listener, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var reader = new Http1BodyReader(upstreamStream, initialBodyBytes, _metrics, timeouts.UpstreamResponseBodyIdleTimeout, ProxyTimeoutKind.UpstreamResponseBodyIdle);
        if (responseHead.Framing.Kind == Http1BodyKind.ContentLength)
        {
            await RelayFixedLengthBodyAsync(reader, clientStream, responseHead.Framing.ContentLength.GetValueOrDefault(), listener.ForwardingBufferBytes, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
        }
        else if (responseHead.Framing.Kind == Http1BodyKind.Chunked)
        {
            await RelayChunkedBodyAsync(reader, clientStream, listener, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
        }
        else if (responseHead.Framing.Kind == Http1BodyKind.CloseDelimited)
        {
            await RelayCloseDelimitedBodyAsync(reader, clientStream, listener.ForwardingBufferBytes, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsValidSwitchingProtocolsResponse(Http1ResponseHead responseHead, UpgradeRequestInfo upgrade)
    {
        if (!HopByHopHeaderPolicy.HasConnectionToken(responseHead.Headers, "upgrade"))
        {
            return false;
        }

        var responseUpgrade = UpgradeRequestPolicy.GetHeaderValue(responseHead.Headers, "Upgrade");
        if (!string.Equals(responseUpgrade, upgrade.Protocol, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!upgrade.IsWebSocket)
        {
            return true;
        }

        var accept = UpgradeRequestPolicy.GetHeaderValue(responseHead.Headers, "Sec-WebSocket-Accept");
        return string.Equals(accept, ComputeWebSocketAccept(upgrade.WebSocketKey!), StringComparison.Ordinal);
    }

    private static string ComputeWebSocketAccept(string webSocketKey)
    {
        var input = Encoding.ASCII.GetBytes(webSocketKey.Trim() + WebSocketAcceptGuid);
        // RFC 6455 section 4.2.2 mandates SHA-1 for this handshake checksum; it does not sign or protect data.
#pragma warning disable CA5350
        var hash = SHA1.HashData(input);
#pragma warning restore CA5350
        return Convert.ToBase64String(hash);
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

    private async ValueTask RelayChunkedBodyAsync(Http1BodyReader reader, Stream destination, RuntimeListener listener, TimeSpan writeTimeout, CancellationToken cancellationToken)
    {
        while (true)
        {
            var chunkLine = await reader.ReadLineWithCrlfAsync(listener.MaxChunkLineBytes, cancellationToken).ConfigureAwait(false);
            await ProxyTimedStreamWriter.WriteAsync(destination, chunkLine, writeTimeout, cancellationToken).ConfigureAwait(false);
            _metrics.AddBytesWritten(chunkLine.Length);
            if (!Http1ChunkSizeParser.TryParseLine(chunkLine.AsSpan(), out var chunkSize))
            {
                throw new IOException("Invalid chunk-size line.");
            }

            if (chunkSize == 0)
            {
                await RelayTrailerSectionAsync(reader, destination, listener.MaxChunkLineBytes, writeTimeout, cancellationToken).ConfigureAwait(false);
                return;
            }

            await RelayFixedLengthBodyAsync(reader, destination, chunkSize, listener.ForwardingBufferBytes, writeTimeout, cancellationToken).ConfigureAwait(false);
            var crlf = await reader.ReadExactAsync(2, cancellationToken).ConfigureAwait(false);
            await ProxyTimedStreamWriter.WriteAsync(destination, crlf, writeTimeout, cancellationToken).ConfigureAwait(false);
            _metrics.AddBytesWritten(crlf.Length);
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
        }
    }

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
}
