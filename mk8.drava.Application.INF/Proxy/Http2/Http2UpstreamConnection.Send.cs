using System.Buffers.Binary;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.Application.INF.Proxy.Http2;

internal sealed partial class Http2UpstreamConnection
{
    private byte[] ClientSettings()
    {
        var settings = new byte[18];
        WriteSetting(settings.AsSpan(0, 6), 1, 0);
        WriteSetting(settings.AsSpan(6, 6), 2, 0);
        WriteSetting(settings.AsSpan(12, 6), 5, (uint)_maxFrameSize);
        return settings;
    }

    private static void WriteSetting(Span<byte> destination, ushort identifier, uint value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, identifier);
        BinaryPrimitives.WriteUInt32BigEndian(destination[2..], value);
    }

    public async ValueTask SendHeadersAsync(IReadOnlyList<ProxyHeaderField> headers, bool endStream, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var block = HpackCodec.EncodeRequestHeaders(headers);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var offset = 0;
            do
            {
                var count = Math.Min(16384, block.Length - offset);
                var type = offset == 0 ? Http2FrameType.Headers : Http2FrameType.Continuation;
                var flags = offset == 0 && endStream ? Http2Flags.EndStream : (byte)0;
                if (offset + count == block.Length) flags |= Http2Flags.EndHeaders;
                await WriteFrameWithoutLockAsync(type, flags, _streamId, block.AsMemory(offset, count), timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
                offset += count;
            } while (offset < block.Length);
        }
        finally { _writeGate.Release(); }
        if (endStream) _requestCompleted.TrySetResult();
    }

    private async ValueTask WriteFrameAsync(Http2FrameType type, byte flags, int streamId, ReadOnlyMemory<byte> payload, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await WriteFrameWithoutLockAsync(type, flags, streamId, payload, timeout, cancellationToken).ConfigureAwait(false); }
        finally { _writeGate.Release(); }
    }
}
