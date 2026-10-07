using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Proxy.Http2;

internal sealed partial class Http2UpstreamConnection
{
    private int _maximumResponseFieldBytes;

    private async ValueTask<Http2UpstreamDataChunk> ReadTrailersAsync(Http2Frame first, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        if ((first.Flags & Http2Flags.EndStream) == 0)
            throw new Http2UpstreamProtocolException("Trailing HTTP/2 HEADERS must end the response stream.");
        using var block = new MemoryStream();
        var frame = first;
        while (true)
        {
            var valid = true;
            var payload = frame.Type == Http2FrameType.Headers ? StripHeaderPaddingAndPriority(frame, out valid) : frame.Payload;
            if (frame.Type == Http2FrameType.Headers && !valid)
                throw new Http2UpstreamProtocolException("Malformed HTTP/2 trailing field block.");
            if (block.Length + payload.Length > _maximumResponseFieldBytes)
                throw new Http2UpstreamProtocolException("HTTP/2 trailing field block exceeded its bound.");
            block.Write(payload.Span);
            if ((frame.Flags & Http2Flags.EndHeaders) != 0) break;
            frame = await ReadFrameAsync(timeouts.UpstreamResponseBodyIdleTimeout, ProxyTimeoutKind.UpstreamResponseBodyIdle, cancellationToken).ConfigureAwait(false)
                ?? throw new Http2UpstreamProtocolException("Upstream closed during HTTP/2 trailing fields.");
            if (frame.Type != Http2FrameType.Continuation || frame.StreamId != _streamId)
                throw new Http2UpstreamProtocolException("Interleaved HTTP/2 trailing field block.");
        }
        if (!HpackCodec.TryDecodeResponseHeaders(block.ToArray(), _maximumResponseFieldBytes, FrameLimits.MaximumHeaderCount, out var fields, out _))
            throw new Http2UpstreamProtocolException("Invalid HPACK trailing fields.");
        if (fields.Count > FrameLimits.MaximumHeaderCount) throw new Http2UpstreamProtocolException("Too many HTTP/2 trailing fields.");
        var bytes = 0;
        List<Header> typed = [];
        foreach (var field in fields)
        {
            Http2ResponseFieldPolicy.Validate(field);
            if (HopByHopHeaderPolicy.IsHopByHopHeader(field.Name))
                throw new Http2UpstreamProtocolException("Invalid HTTP/2 trailing field name.");
            bytes = checked(bytes + System.Text.Encoding.UTF8.GetByteCount(field.Name) + System.Text.Encoding.UTF8.GetByteCount(field.Value));
            if (bytes > _maximumResponseFieldBytes) throw new Http2UpstreamProtocolException("Decoded trailing fields exceeded their bound.");
            typed.Add(new Header { Name = field.Name, Value = field.Value });
        }
        try { FrameLimits.ValidateHeaders(typed, trailers: true); }
        catch (InvalidDataException exception) { throw new Http2UpstreamProtocolException("Forbidden or malformed HTTP/2 trailing fields.", exception); }
        return new Http2UpstreamDataChunk([], true, fields);
    }
}
