using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
#pragma warning disable CA1416
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using System.Globalization;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.INF.Proxy.Forwarding;

namespace Mk8.Drava.Application.INF.Proxy.Http3;
internal sealed partial class Http3UpstreamConnection : IAsyncDisposable
{
    private const int MaxFramePayloadBytes = 1024 * 1024;
    private static readonly SslApplicationProtocol Http3Alpn = new("h3");
    private readonly ProxyMetrics _metrics;
    private readonly int _maxFramePayloadBytes;
    private readonly Http3UpstreamPooledConnection? _pooledConnection;
    private readonly QuicStream? _controlStream;
    private bool _connectionUsable = true;
    private Http3UpstreamConnection(QuicConnection connection, QuicStream stream, ProxyMetrics metrics, int maxFramePayloadBytes, QuicStream? controlStream, Http3UpstreamPooledConnection? pooledConnection)
    {
        Connection = connection;
        Stream = stream;
        _metrics = metrics;
        _maxFramePayloadBytes = Math.Clamp(maxFramePayloadBytes, 16 * 1024, MaxFramePayloadBytes);
        _controlStream = controlStream;
        _pooledConnection = pooledConnection;
    }

    private QuicConnection Connection { get; }
    private QuicStream Stream { get; }

    public static async ValueTask<Http3UpstreamConnection> ConnectAsync(UpstreamTransportEndpoint endpoint, RuntimeTimeouts timeouts, ProxyMetrics metrics, int maxFramePayloadBytes, CancellationToken cancellationToken)
    {
        metrics.UpstreamHttp3ConnectionAttempted();
        if (!QuicConnection.IsSupported)
        {
            throw new Http3UpstreamProtocolException("The current runtime does not support QUIC client connections.", Http3UpstreamFailureKind.ConnectFailure);
        }

        var remoteEndPoint = await ResolveEndPointAsync(endpoint, cancellationToken).ConfigureAwait(false);
        Http3UpstreamTransport? transport = null;
        QuicStream? stream = null;
        var streamStarted = false;
        try
        {
            transport = await OpenTransportAsync(endpoint, remoteEndPoint, timeouts, metrics, cancellationToken).ConfigureAwait(false);
            stream = await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await transport.Connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, timeoutToken).ConfigureAwait(false), timeouts.UpstreamConnectTimeout, ProxyTimeoutKind.UpstreamConnect, cancellationToken).ConfigureAwait(false);
            metrics.UpstreamHttp3StreamStarted();
            streamStarted = true;
            return new Http3UpstreamConnection(transport.Connection, stream, metrics, maxFramePayloadBytes, transport.ControlStream, pooledConnection: null);
        }
        catch (Http3UpstreamProtocolException)
        {
            metrics.UpstreamHttp3ConnectionFailed();
            await DisposePartialConnectionAsync(transport, stream, metrics, streamStarted).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)when (exception is AuthenticationException or IOException or QuicException)
        {
            metrics.UpstreamHttp3ConnectionFailed();
            await DisposePartialConnectionAsync(transport, stream, metrics, streamStarted).ConfigureAwait(false);
            throw new Http3UpstreamProtocolException("Failed to connect to the upstream HTTP/3 endpoint.", Http3UpstreamFailureKind.ConnectFailure, exception);
        }
    }

    internal static async ValueTask<Http3UpstreamTransport> OpenTransportAsync(UpstreamTransportEndpoint endpoint, RuntimeTimeouts timeouts, ProxyMetrics metrics, CancellationToken cancellationToken)
    {
        metrics.UpstreamHttp3ConnectionAttempted();
        if (!QuicConnection.IsSupported)
        {
            metrics.UpstreamHttp3ConnectionFailed();
            throw new Http3UpstreamProtocolException("The current runtime does not support QUIC client connections.", Http3UpstreamFailureKind.ConnectFailure);
        }

        var remoteEndPoint = await ResolveEndPointAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return await OpenTransportAsync(endpoint, remoteEndPoint, timeouts, metrics, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask<Http3UpstreamConnection> OpenStreamAsync(Http3UpstreamPooledConnection pooledConnection, RuntimeTimeouts timeouts, ProxyMetrics metrics, int maxFramePayloadBytes, CancellationToken cancellationToken)
    {
        QuicStream? stream = null;
        var streamStarted = false;
        try
        {
            stream = await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await pooledConnection.Connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, timeoutToken).ConfigureAwait(false), timeouts.UpstreamConnectTimeout, ProxyTimeoutKind.UpstreamConnect, cancellationToken).ConfigureAwait(false);
            metrics.UpstreamHttp3StreamStarted();
            streamStarted = true;
            return new Http3UpstreamConnection(pooledConnection.Connection, stream, metrics, maxFramePayloadBytes, controlStream: null, pooledConnection);
        }
        catch (Exception exception)when (exception is QuicException or IOException)
        {
            if (stream is not null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }

            if (streamStarted)
            {
                metrics.UpstreamHttp3StreamEnded();
            }

            pooledConnection.MarkUnusable();
            pooledConnection.ReleaseStream(connectionUsable: false);
            throw new Http3UpstreamProtocolException("Failed to open an upstream HTTP/3 request stream.", Http3UpstreamFailureKind.ConnectFailure, exception);
        }
    }

    public async ValueTask SendHeadersAsync(IReadOnlyList<ProxyHeaderField> headers, bool endStream, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        try
        {
            var block = Http3Codec.EncodeHeaderBlock(headers);
            using var memory = new MemoryStream();
            Http3Codec.WriteFrame(memory, Http3Codec.HeadersFrame, block);
            await WriteWithTimeoutAsync(memory.ToArray(), endStream, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
            if (endStream) _requestCompleted.TrySetResult();
        }
        catch
        {
            MarkConnectionUnusable();
            throw;
        }
    }

    public async ValueTask SendDataAsync(ReadOnlyMemory<byte> body, bool endStream, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        try
        {
            var remaining = body;
            while (remaining.Length > 0)
            {
                var chunkLength = Math.Min(_maxFramePayloadBytes, remaining.Length);
                var final = chunkLength == remaining.Length && endStream;
                using var memory = new MemoryStream();
                Http3Codec.WriteFrame(memory, Http3Codec.DataFrame, remaining[..chunkLength].Span);
                await WriteWithTimeoutAsync(memory.ToArray(), final, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
                remaining = remaining[chunkLength..];
            }

            if (body.Length == 0 && endStream)
            {
                using var memory = new MemoryStream();
                Http3Codec.WriteFrame(memory, Http3Codec.DataFrame, ReadOnlySpan<byte>.Empty);
                await WriteWithTimeoutAsync(memory.ToArray(), completeWrites: true, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
            }
            if (endStream) _requestCompleted.TrySetResult();
        }
        catch
        {
            MarkConnectionUnusable();
            throw;
        }
    }

    public ValueTask<Http3UpstreamResponseHead> ReadResponseHeadAsync(int maxHeaderListBytes, RuntimeTimeouts timeouts, CancellationToken cancellationToken) =>
        ReadResponseHeadAsync(maxHeaderListBytes, timeouts, informationalHead: null, cancellationToken);

    public async ValueTask<Http3UpstreamResponseHead> ReadResponseHeadAsync(int maxHeaderListBytes, RuntimeTimeouts timeouts,
        Func<Http3UpstreamResponseHead, CancellationToken, ValueTask>? informationalHead, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHeaderListBytes, 1);
        var maximumBytes = _maximumResponseFieldBytes = Math.Min(maxHeaderListBytes, Mk8.Drava.Transport.Protocol.FrameLimits.MaximumHeaderBytes);
        var informational = 0;
        try
        {
            while (true)
            {
                var frame = await ReadHeadFrameAsync(timeouts.UpstreamResponseHeadTimeout, cancellationToken).ConfigureAwait(false);
                if (frame.EndStream)
                {
                    throw new Http3UpstreamProtocolException("Upstream closed before HTTP/3 response headers were received.");
                }

                if (frame.Type == Http3Codec.DataFrame)
                {
                    throw new Http3UpstreamProtocolException("Upstream sent HTTP/3 response DATA before response headers.");
                }

                if (frame.Type != Http3Codec.HeadersFrame)
                {
                    throw new Http3UpstreamProtocolException("Upstream sent an unsupported HTTP/3 response frame before headers.");
                }

                _headFramesObserved++;
                var decoded = DecodeResponseHeaders(frame.Payload.Span, maximumBytes);
                if (decoded.StatusCode is >= 100 and < 200)
                {
                    if (decoded.StatusCode == 101 || ++informational > 8
                        || decoded.Headers.Any(static field => string.Equals(field.Name, "content-length", StringComparison.OrdinalIgnoreCase)))
                        throw new Http3UpstreamProtocolException("Malformed HTTP/3 informational response.");
                    if (informationalHead is not null) await informationalHead(decoded, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                return decoded;
            }
        }
        catch
        {
            MarkConnectionUnusable();
            throw;
        }
    }

    public async ValueTask<Http3UpstreamDataChunk> ReadDataAsync(RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        if (_responseEnded) return new Http3UpstreamDataChunk([], EndStream: true);
        try
        {
            while (true)
            {
                var frame = await ReadResponseFrameAsync(timeouts.UpstreamResponseBodyIdleTimeout, ProxyTimeoutKind.UpstreamResponseBodyIdle, cancellationToken).ConfigureAwait(false);
                if (frame.EndStream)
                {
                    _responseEnded = true;
                    return new Http3UpstreamDataChunk([], EndStream: true);
                }

                if (frame.Type == Http3Codec.DataFrame)
                {
                    return new Http3UpstreamDataChunk(frame.Payload.ToArray(), EndStream: false);
                }

                if (frame.Type == Http3Codec.HeadersFrame)
                {
                    return await ReadResponseTrailersAsync(frame.Payload, timeouts, cancellationToken).ConfigureAwait(false);
                }

                throw new Http3UpstreamProtocolException("Upstream sent an unsupported HTTP/3 response frame.");
            }
        }
        catch
        {
            MarkConnectionUnusable();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Stream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _metrics.UpstreamHttp3StreamEnded();
            if (_pooledConnection is not null)
            {
                _pooledConnection.ReleaseStream(_connectionUsable);
            }
            else
            {
                try
                {
                    if (_controlStream is not null)
                    {
                        await _controlStream.DisposeAsync().ConfigureAwait(false);
                    }

                    await Connection.CloseAsync(0, CancellationToken.None).ConfigureAwait(false);
                }
                catch (QuicException)
                {
                }

                await Connection.DisposeAsync().ConfigureAwait(false);
                _metrics.UpstreamHttp3ConnectionClosed();
                _metrics.UpstreamHttp3PoolConnectionClosed();
            }
        }
    }

    private static async ValueTask<Http3UpstreamTransport> OpenTransportAsync(UpstreamTransportEndpoint endpoint, IPEndPoint remoteEndPoint, RuntimeTimeouts timeouts, ProxyMetrics metrics, CancellationToken cancellationToken)
    {
        QuicConnection? connection = null;
        try
        {
            connection = await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await QuicConnection.ConnectAsync(new QuicClientConnectionOptions { RemoteEndPoint = remoteEndPoint, ClientAuthenticationOptions = new SslClientAuthenticationOptions { TargetHost = endpoint.EffectiveSniHost, EnabledSslProtocols = SslProtocols.Tls13, ApplicationProtocols = [Http3Alpn], CertificateRevocationCheckMode = X509RevocationMode.NoCheck, RemoteCertificateValidationCallback = endpoint.ValidateCertificate ? null : static (_, _, _, _) => true }, MaxInboundBidirectionalStreams = 16, MaxInboundUnidirectionalStreams = 4, IdleTimeout = timeouts.UpstreamIdleConnectionLifetime, HandshakeTimeout = timeouts.UpstreamConnectTimeout, DefaultCloseErrorCode = 0x100, DefaultStreamErrorCode = 0x100 }, timeoutToken).ConfigureAwait(false), timeouts.UpstreamConnectTimeout, ProxyTimeoutKind.UpstreamConnect, cancellationToken).ConfigureAwait(false);
            metrics.UpstreamHttp3ConnectionSucceeded();
            metrics.UpstreamHttp3ConnectionOpened();
            metrics.UpstreamHttp3PoolConnectionOpened();
            var controlStream = await SendSettingsAsync(connection, timeouts, cancellationToken).ConfigureAwait(false);
            return new Http3UpstreamTransport(connection, controlStream);
        }
        catch
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                metrics.UpstreamHttp3ConnectionClosed();
                metrics.UpstreamHttp3PoolConnectionClosed();
            }

            throw;
        }
    }

    private static async ValueTask<QuicStream> SendSettingsAsync(QuicConnection connection, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var control = await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, timeoutToken).ConfigureAwait(false), timeouts.UpstreamConnectTimeout, ProxyTimeoutKind.UpstreamConnect, cancellationToken).ConfigureAwait(false);
        using var payload = new MemoryStream();
        Http3Codec.WriteVarInt(payload, Http3Codec.ControlStream);
        using var settings = new MemoryStream();
        Http3Codec.WriteVarInt(settings, Http3Codec.QpackMaxTableCapacitySetting);
        Http3Codec.WriteVarInt(settings, 0);
        Http3Codec.WriteVarInt(settings, Http3Codec.QpackBlockedStreamsSetting);
        Http3Codec.WriteVarInt(settings, 0);
        Http3Codec.WriteFrame(payload, Http3Codec.SettingsFrame, settings.ToArray());
        await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await control.WriteAsync(payload.ToArray(), completeWrites: false, timeoutToken).ConfigureAwait(false), timeouts.DownstreamWriteTimeout, ProxyTimeoutKind.DownstreamWrite, cancellationToken).ConfigureAwait(false);
        return control;
    }

    private async ValueTask<Http3FrameReadResult> ReadFrameAsync(TimeSpan timeout, ProxyTimeoutKind timeoutKind, CancellationToken cancellationToken)
    {
        var type = await ReadVarIntAsync(timeout, timeoutKind, cancellationToken).ConfigureAwait(false);
        if (!type.Success)
        {
            return Http3FrameReadResult.End;
        }

        var length = await ReadVarIntAsync(timeout, timeoutKind, cancellationToken).ConfigureAwait(false);
        if (!length.Success || length.Value < 0 || length.Value > _maxFramePayloadBytes)
        {
            throw new Http3UpstreamProtocolException("Upstream HTTP/3 frame was malformed or exceeded the configured maximum size.");
        }

        var payload = length.Value == 0 ? [] : await ReadExactAsync((int)length.Value, timeout, timeoutKind, cancellationToken).ConfigureAwait(false);
        return new Http3FrameReadResult(false, type.Value, payload);
    }

    private void MarkConnectionUnusable()
    {
        _connectionUsable = false;
        _pooledConnection?.MarkUnusable();
    }

    private async ValueTask<Http3VarIntReadResult> ReadVarIntAsync(TimeSpan timeout, ProxyTimeoutKind timeoutKind, CancellationToken cancellationToken)
    {
        var first = await ReadExactAsync(1, timeout, timeoutKind, cancellationToken, allowEnd: true).ConfigureAwait(false);
        if (first.Length == 0)
        {
            return Http3VarIntReadResult.Failure;
        }

        var length = 1 << (first[0] >> 6);
        long value = first[0] & 0x3f;
        if (length == 1)
        {
            return new Http3VarIntReadResult(true, value);
        }

        var rest = await ReadExactAsync(length - 1, timeout, timeoutKind, cancellationToken).ConfigureAwait(false);
        foreach (var next in rest)
        {
            value = (value << 8) | next;
        }

        return new Http3VarIntReadResult(true, value);
    }

    private async ValueTask<byte[]> ReadExactAsync(int length, TimeSpan timeout, ProxyTimeoutKind timeoutKind, CancellationToken cancellationToken, bool allowEnd = false)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await Stream.ReadAsync(buffer.AsMemory(offset, length - offset), timeoutToken).ConfigureAwait(false), timeout, timeoutKind, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return allowEnd && offset == 0 ? [] : throw new Http3UpstreamProtocolException("Upstream closed mid HTTP/3 frame.");
            }

            _metrics.AddBytesRead(read);
            offset += read;
        }

        return buffer;
    }

    private async ValueTask WriteWithTimeoutAsync(ReadOnlyMemory<byte> bytes, bool completeWrites, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await Stream.WriteAsync(bytes, completeWrites, timeoutToken).ConfigureAwait(false), timeout, ProxyTimeoutKind.DownstreamWrite, cancellationToken).ConfigureAwait(false);
        _metrics.AddBytesWritten(bytes.Length);
    }

    private static Http3UpstreamResponseHead DecodeResponseHeaders(ReadOnlySpan<byte> block, int maxHeaderListBytes)
    {
        if (!Http3Codec.TryDecodeHeaderBlock(block, maxHeaderListBytes, out var headers, out var reason)
            || headers.Count > Mk8.Drava.Transport.Protocol.FrameLimits.MaximumHeaderCount)
        {
            throw new Http3UpstreamProtocolException($"Upstream sent invalid HTTP/3 response headers: {reason}.");
        }

        int? statusCode = null;
        List<ProxyHeaderField> regularHeaders = [];
        foreach (var header in headers)
        {
            if (string.Equals(header.Name, ":status", StringComparison.Ordinal))
            {
                if (statusCode.HasValue || regularHeaders.Count != 0 || header.Value.Length != 3
                    || !int.TryParse(header.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 100 or > 599)
                {
                    throw new Http3UpstreamProtocolException("Upstream sent an invalid HTTP/3 :status pseudo-header.");
                }

                statusCode = parsed;
                continue;
            }

            if (header.Name.StartsWith(':'))
            {
                throw new Http3UpstreamProtocolException("Upstream sent an invalid HTTP/3 response pseudo-header.");
            }

            if (!FramedResponseFieldPolicy.IsValid(header) || HopByHopHeaderPolicy.IsHopByHopHeader(header.Name))
            {
                throw new Http3UpstreamProtocolException("Upstream sent a forbidden HTTP/3 hop-by-hop response header.");
            }

            regularHeaders.Add(new ProxyHeaderField(header.Name, header.Value));
        }

        if (!statusCode.HasValue)
        {
            throw new Http3UpstreamProtocolException("Upstream response did not include an HTTP/3 :status pseudo-header.");
        }

        return new Http3UpstreamResponseHead(statusCode.Value, regularHeaders);
    }

    private static async ValueTask<IPEndPoint> ResolveEndPointAsync(UpstreamTransportEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(endpoint.Address, out var address))
        {
            return new IPEndPoint(address, endpoint.Port);
        }

        var addresses = await System.Net.Dns.GetHostAddressesAsync(endpoint.Address, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0)
        {
            throw new IOException($"Unable to resolve upstream '{endpoint.Name}' at {endpoint.Address}.");
        }

        return new IPEndPoint(addresses[0], endpoint.Port);
    }

    private static async ValueTask DisposePartialConnectionAsync(Http3UpstreamTransport? transport, QuicStream? stream, ProxyMetrics metrics, bool streamStarted)
    {
        if (stream is not null)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        if (streamStarted)
        {
            metrics.UpstreamHttp3StreamEnded();
        }

        if (transport is not null)
        {
            try
            {
                await transport.ControlStream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await transport.Connection.DisposeAsync().ConfigureAwait(false);
            }

            metrics.UpstreamHttp3ConnectionClosed();
            metrics.UpstreamHttp3PoolConnectionClosed();
        }
    }

    private readonly record struct Http3FrameReadResult(bool EndStream, long Type, ReadOnlyMemory<byte> Payload)
    {
        public static Http3FrameReadResult End { get; } = new(true, 0, ReadOnlyMemory<byte>.Empty);
    }

    private readonly record struct Http3VarIntReadResult(bool Success, long Value)
    {
        public static Http3VarIntReadResult Failure { get; } = new(false, 0);
    }
}
