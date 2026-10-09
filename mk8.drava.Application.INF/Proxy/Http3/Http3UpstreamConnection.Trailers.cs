using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Proxy.Http3;

internal sealed partial class Http3UpstreamConnection
{
    private int _maximumResponseFieldBytes = FrameLimits.MaximumHeaderBytes;
    private bool _responseEnded;

    private async ValueTask<Http3UpstreamDataChunk> ReadResponseTrailersAsync(ReadOnlyMemory<byte> block,
        RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var fields = DecodeTrailingFields(block.Span, _maximumResponseFieldBytes);
        var next = await ReadFrameAsync(timeouts.UpstreamResponseBodyIdleTimeout,
            ProxyTimeoutKind.UpstreamResponseBodyIdle, cancellationToken).ConfigureAwait(false);
        if (!next.EndStream) throw new Http3UpstreamProtocolException("HTTP/3 frames followed trailing fields.");
        _responseEnded = true;
        return new Http3UpstreamDataChunk([], true, fields);
    }

    private static IReadOnlyList<ProxyHeaderField> DecodeTrailingFields(ReadOnlySpan<byte> block, int maximumBytes)
    {
        if (!Http3Codec.TryDecodeHeaderBlock(block, maximumBytes, out var fields, out _)
            || fields.Count > FrameLimits.MaximumHeaderCount)
            throw new Http3UpstreamProtocolException("Invalid or oversized QPACK trailing fields.");
        var bytes = 0;
        List<Header> typed = [];
        foreach (var field in fields)
        {
            if (!FramedResponseFieldPolicy.IsValid(field) || HopByHopHeaderPolicy.IsHopByHopHeader(field.Name))
                throw new Http3UpstreamProtocolException("Invalid HTTP/3 trailing field.");
            bytes = checked(bytes + System.Text.Encoding.UTF8.GetByteCount(field.Name) + System.Text.Encoding.UTF8.GetByteCount(field.Value));
            if (bytes > maximumBytes) throw new Http3UpstreamProtocolException("Decoded trailing fields exceeded their bound.");
            typed.Add(new Header { Name = field.Name, Value = field.Value });
        }
        try { FrameLimits.ValidateHeaders(typed, trailers: true); }
        catch (InvalidDataException exception) { throw new Http3UpstreamProtocolException("Forbidden or malformed HTTP/3 trailing fields.", exception); }
        return fields;
    }
}
