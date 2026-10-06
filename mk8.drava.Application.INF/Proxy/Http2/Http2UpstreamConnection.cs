using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using System.Buffers.Binary;
using Mk8.Drava.Application.INF.Proxy.Forwarding;

namespace Mk8.Drava.Application.INF.Proxy.Http2;
internal sealed class Http2UpstreamConnection
{
    private static readonly byte[] ClientPreface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
    private readonly Stream _stream;
    private readonly ProxyMetrics _metrics;
    private readonly int _streamId;
    private readonly int _maxFrameSize;
    public Http2UpstreamConnection(Stream stream, ProxyMetrics metrics, int streamId = 1, int maxFrameSize = 16 * 1024)
    {
        _stream = stream;
        _metrics = metrics;
        _streamId = streamId;
        _maxFrameSize = maxFrameSize;
    }

    public async ValueTask InitializeAsync(RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        await WriteWithTimeoutAsync(ClientPreface, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
        await WriteFrameAsync(Http2FrameType.Settings, 0, 0, ReadOnlyMemory<byte>.Empty, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var frame = await ReadFrameAsync(timeouts.UpstreamResponseHeadTimeout, ProxyTimeoutKind.UpstreamResponseHead, cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                throw new Http2UpstreamProtocolException("Upstream closed before HTTP/2 SETTINGS were received.");
            }

            if (frame.Value.Type == Http2FrameType.Settings)
            {
                if ((frame.Value.Flags & Http2Flags.Ack) == 0)
                {
                    await WriteFrameAsync(Http2FrameType.Settings, Http2Flags.Ack, 0, ReadOnlyMemory<byte>.Empty, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
                    return;
                }

                continue;
            }

            if (frame.Value.Type == Http2FrameType.GoAway)
            {
                throw new Http2UpstreamProtocolException("Upstream sent GOAWAY before the HTTP/2 connection was initialized.");
            }
        }
    }

    public ValueTask SendHeadersAsync(IReadOnlyList<ProxyHeaderField> headers, bool endStream, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var block = HpackCodec.EncodeRequestHeaders(headers);
        return WriteFrameAsync(Http2FrameType.Headers, endStream ? (byte)(Http2Flags.EndHeaders | Http2Flags.EndStream) : Http2Flags.EndHeaders, _streamId, block, timeouts.DownstreamWriteTimeout, cancellationToken);
    }

    public async ValueTask SendDataAsync(ReadOnlyMemory<byte> body, bool endStream, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var remaining = body;
        while (remaining.Length > 0)
        {
            var chunkLength = Math.Min(_maxFrameSize, remaining.Length);
            var final = chunkLength == remaining.Length && endStream;
            await WriteFrameAsync(Http2FrameType.Data, final ? Http2Flags.EndStream : (byte)0, _streamId, remaining[..chunkLength], timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
            remaining = remaining[chunkLength..];
        }

        if (body.Length == 0 && endStream)
        {
            await WriteFrameAsync(Http2FrameType.Data, Http2Flags.EndStream, _streamId, ReadOnlyMemory<byte>.Empty, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<Http2UpstreamResponseHead> ReadResponseHeadAsync(int maxHeaderListBytes, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        using var headerBlock = new MemoryStream();
        while (true)
        {
            var frame = await ReadFrameAsync(timeouts.UpstreamResponseHeadTimeout, ProxyTimeoutKind.UpstreamResponseHead, cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                throw new Http2UpstreamProtocolException("Upstream closed before response headers were received.");
            }

            if (await HandleConnectionFrameAsync(frame.Value, timeouts, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (frame.Value.StreamId != _streamId)
            {
                continue;
            }

            if (frame.Value.Type is Http2FrameType.Headers or Http2FrameType.Continuation)
            {
                var valid = true;
                var payload = frame.Value.Type == Http2FrameType.Headers ? StripHeaderPaddingAndPriority(frame.Value, out valid) : frame.Value.Payload;
                if (!valid)
                {
                    throw new Http2UpstreamProtocolException("Upstream sent invalid HTTP/2 response headers.");
                }

                headerBlock.Write(payload.Span);
                if (headerBlock.Length > maxHeaderListBytes)
                {
                    throw new Http2UpstreamProtocolException("Upstream HTTP/2 response header block exceeded the configured limit.");
                }

                if ((frame.Value.Flags & Http2Flags.EndHeaders) == 0)
                {
                    continue;
                }

                var decoded = DecodeResponseHeaders(headerBlock.ToArray());
                if (decoded.StatusCode is >= 100 and < 200)
                {
                    headerBlock.SetLength(0);
                    continue;
                }

                return decoded with
                {
                    EndStream = (frame.Value.Flags & Http2Flags.EndStream) != 0
                };
            }

            if (frame.Value.Type == Http2FrameType.RstStream)
            {
                throw new Http2UpstreamProtocolException("Upstream reset the HTTP/2 response stream.");
            }

            if (frame.Value.Type == Http2FrameType.Data)
            {
                throw new Http2UpstreamProtocolException("Upstream sent HTTP/2 response data before response headers.");
            }
        }
    }

    public async ValueTask<Http2UpstreamDataChunk> ReadDataAsync(RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        while (true)
        {
            var frame = await ReadFrameAsync(timeouts.UpstreamResponseBodyIdleTimeout, ProxyTimeoutKind.UpstreamResponseBodyIdle, cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                throw new Http2UpstreamProtocolException("Upstream closed before ending the HTTP/2 response stream.");
            }

            if (await HandleConnectionFrameAsync(frame.Value, timeouts, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (frame.Value.StreamId != _streamId)
            {
                continue;
            }

            if (frame.Value.Type == Http2FrameType.Data)
            {
                var payload = StripDataPadding(frame.Value, out var valid);
                if (!valid)
                {
                    throw new Http2UpstreamProtocolException("Upstream sent invalid padded HTTP/2 DATA.");
                }

                // END_STREAM needs no further per-stream credit. Deliver the final DATA
                // before touching a peer that can already be closing its connection.
                if (payload.Length > 0 && (frame.Value.Flags & Http2Flags.EndStream) == 0)
                {
                    await SendWindowUpdateAsync(0, payload.Length, timeouts, cancellationToken).ConfigureAwait(false);
                    await SendWindowUpdateAsync(_streamId, payload.Length, timeouts, cancellationToken).ConfigureAwait(false);
                }

                return new Http2UpstreamDataChunk(payload.ToArray(), (frame.Value.Flags & Http2Flags.EndStream) != 0);
            }

            if (frame.Value.Type is Http2FrameType.Headers or Http2FrameType.Continuation)
            {
                if ((frame.Value.Flags & Http2Flags.EndStream) != 0)
                {
                    return new Http2UpstreamDataChunk([], true);
                }

                continue;
            }

            if (frame.Value.Type == Http2FrameType.RstStream)
            {
                throw new Http2UpstreamProtocolException("Upstream reset the HTTP/2 response stream.");
            }
        }
    }

    private async ValueTask<bool> HandleConnectionFrameAsync(Http2Frame frame, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        if (frame.Type == Http2FrameType.Settings)
        {
            if ((frame.Flags & Http2Flags.Ack) == 0)
            {
                await WriteFrameAsync(Http2FrameType.Settings, Http2Flags.Ack, 0, ReadOnlyMemory<byte>.Empty, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        if (frame.Type == Http2FrameType.Ping)
        {
            if ((frame.Flags & Http2Flags.Ack) == 0 && frame.Payload.Length == 8)
            {
                await WriteFrameAsync(Http2FrameType.Ping, Http2Flags.Ack, 0, frame.Payload, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        if (frame.Type == Http2FrameType.WindowUpdate)
        {
            return true;
        }

        if (frame.Type == Http2FrameType.GoAway)
        {
            throw new Http2UpstreamProtocolException("Upstream sent HTTP/2 GOAWAY.");
        }

        return false;
    }

    private async ValueTask<Http2Frame?> ReadFrameAsync(TimeSpan timeout, ProxyTimeoutKind timeoutKind, CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(9, timeout, timeoutKind, cancellationToken).ConfigureAwait(false);
        if (header.Length == 0)
        {
            return null;
        }

        var length = header[0] << 16 | header[1] << 8 | header[2];
        if (length > _maxFrameSize)
        {
            throw new Http2UpstreamProtocolException("Upstream HTTP/2 frame exceeded the configured maximum frame size.");
        }

        var payload = length == 0 ? [] : await ReadExactAsync(length, timeout, timeoutKind, cancellationToken).ConfigureAwait(false);
        return new Http2Frame((Http2FrameType)header[3], header[4], (int)(BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5, 4)) & 0x7fffffff), payload);
    }

    private async ValueTask<byte[]> ReadExactAsync(int length, TimeSpan timeout, ProxyTimeoutKind timeoutKind, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await _stream.ReadAsync(buffer.AsMemory(offset, length - offset), timeoutToken).ConfigureAwait(false), timeout, timeoutKind, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return offset == 0 ? [] : throw new Http2UpstreamProtocolException("Upstream closed mid HTTP/2 frame.");
            }

            _metrics.AddBytesRead(read);
            offset += read;
        }

        return buffer;
    }

    private ValueTask SendWindowUpdateAsync(int streamId, int size, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)size);
        return WriteFrameAsync(Http2FrameType.WindowUpdate, 0, streamId, payload, timeouts.DownstreamWriteTimeout, cancellationToken);
    }

    private async ValueTask WriteFrameAsync(Http2FrameType type, byte flags, int streamId, ReadOnlyMemory<byte> payload, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var header = new byte[9];
        header[0] = (byte)((payload.Length >> 16) & 0xff);
        header[1] = (byte)((payload.Length >> 8) & 0xff);
        header[2] = (byte)(payload.Length & 0xff);
        header[3] = (byte)type;
        header[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(5, 4), (uint)streamId & 0x7fffffff);
        await WriteWithTimeoutAsync(header, timeout, cancellationToken).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await WriteWithTimeoutAsync(payload, timeout, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask WriteWithTimeoutAsync(ReadOnlyMemory<byte> bytes, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await ProxyTimeoutPolicy.RunAsync(async timeoutToken => await _stream.WriteAsync(bytes, timeoutToken).ConfigureAwait(false), timeout, ProxyTimeoutKind.DownstreamWrite, cancellationToken).ConfigureAwait(false);
        _metrics.AddBytesWritten(bytes.Length);
    }

    private static Http2UpstreamResponseHead DecodeResponseHeaders(byte[] block)
    {
        if (!HpackCodec.TryDecodeRequestHeaders(block, out var headers, out var reason))
        {
            throw new Http2UpstreamProtocolException($"Upstream sent invalid HPACK response headers: {reason}.");
        }

        int? statusCode = null;
        List<ProxyHeaderField> regularHeaders = [];
        foreach (var header in headers)
        {
            if (string.Equals(header.Name, ":status", StringComparison.Ordinal))
            {
                if (!int.TryParse(header.Value, out var parsed) || parsed is < 100 or > 599)
                {
                    throw new Http2UpstreamProtocolException("Upstream sent an invalid HTTP/2 :status pseudo-header.");
                }

                statusCode = parsed;
                continue;
            }

            if (header.Name.StartsWith(':'))
            {
                throw new Http2UpstreamProtocolException("Upstream sent an invalid HTTP/2 response pseudo-header.");
            }

            if (HopByHopHeaderPolicy.IsHopByHopHeader(header.Name))
            {
                throw new Http2UpstreamProtocolException("Upstream sent a forbidden HTTP/2 hop-by-hop response header.");
            }

            regularHeaders.Add(new ProxyHeaderField(header.Name, header.Value));
        }

        if (!statusCode.HasValue)
        {
            throw new Http2UpstreamProtocolException("Upstream response did not include an HTTP/2 :status pseudo-header.");
        }

        return new Http2UpstreamResponseHead(statusCode.Value, regularHeaders, EndStream: false);
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

    private readonly record struct Http2Frame(Http2FrameType Type, byte Flags, int StreamId, ReadOnlyMemory<byte> Payload);
    private enum Http2FrameType : byte
    {
        Data = 0x0,
        Headers = 0x1,
        Priority = 0x2,
        RstStream = 0x3,
        Settings = 0x4,
        Ping = 0x6,
        GoAway = 0x7,
        WindowUpdate = 0x8,
        Continuation = 0x9
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
