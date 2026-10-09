using System.Net.Quic;
using System.Runtime.Versioning;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
internal sealed class DevelopmentHttp3DuplexPeer(DevelopmentPendingQuicPeer listener)
{
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void FinishResponse() => _finished.TrySetResult();

    public async Task RespondAsync(bool reject, int expectedBodyBytes, CancellationToken cancellationToken)
    {
        var connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
        await using var connectionLifetime = connection.ConfigureAwait(false);
        List<QuicStream> controls = [];
        try
        {
            while (true)
            {
                var stream = await connection.AcceptInboundStreamAsync(cancellationToken).ConfigureAwait(false);
                if (stream.Type == QuicStreamType.Bidirectional)
                {
                    await using var streamLifetime = stream.ConfigureAwait(false);
                    await RespondToRequestAsync(stream, reject, expectedBodyBytes, cancellationToken).ConfigureAwait(false);
                    await _finished.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                controls.Add(stream);
            }
        }
        finally
        {
            for (var index = 0; index < controls.Count; index++) await controls[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task RespondToRequestAsync(QuicStream stream, bool reject, int expectedBodyBytes,
        CancellationToken cancellationToken)
    {
        var head = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        Assert.Equal(Http3Codec.HeadersFrame, head.Type);
        Assert.True(Http3Codec.TryDecodeHeaderBlock(head.Payload.Span, 4096, out var fields, out var reason), reason);
        Assert.Contains(fields, static field => field.Name.Equals(":method", StringComparison.Ordinal) && field.Value.Equals("POST", StringComparison.Ordinal));
        using var responseHead = new MemoryStream();
        Http3Codec.WriteFrame(responseHead, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([
            new(":status", reject ? "413" : "200"), new("content-length", reject ? "8" : expectedBodyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture))]));
        if (reject) Http3Codec.WriteFrame(responseHead, Http3Codec.DataFrame, "rejected"u8);
        await stream.WriteAsync(responseHead.ToArray(), completeWrites: reject, cancellationToken).ConfigureAwait(false);
        if (reject) return;
        var received = 0;
        while (true)
        {
            var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (frame.EndStream) break;
            Assert.Equal(Http3Codec.DataFrame, frame.Type);
            received = checked(received + frame.Payload.Length);
            Assert.InRange(received, 0, expectedBodyBytes);
            using var response = new MemoryStream();
            Http3Codec.WriteFrame(response, Http3Codec.DataFrame, frame.Payload.Span);
            await stream.WriteAsync(response.ToArray(), completeWrites: false, cancellationToken).ConfigureAwait(false);
        }
        Assert.Equal(expectedBodyBytes, received);
        stream.CompleteWrites();
    }

    private static async ValueTask<Http3FrameReadResult> ReadFrameAsync(
        QuicStream stream,
        CancellationToken cancellationToken)
    {
        var type = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!type.Success)
        {
            return Http3FrameReadResult.End;
        }

        var length = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!length.Success || length.Value < 0 || length.Value > 1024 * 1024)
        {
            throw new IOException("Invalid HTTP/3 frame length.");
        }

        var payload = length.Value == 0
            ? []
            : await ReadExactAsync(stream, (int)length.Value, cancellationToken).ConfigureAwait(false);
        return new Http3FrameReadResult(false, type.Value, payload);
    }

    private static async ValueTask<Http3VarIntReadResult> ReadVarIntAsync(
        QuicStream stream,
        CancellationToken cancellationToken)
    {
        var first = await ReadExactAsync(stream, 1, cancellationToken, allowEnd: true).ConfigureAwait(false);
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

        var rest = await ReadExactAsync(stream, length - 1, cancellationToken).ConfigureAwait(false);
        foreach (var next in rest)
        {
            value = (value << 8) | next;
        }

        return new Http3VarIntReadResult(true, value);
    }

    private static async ValueTask<byte[]> ReadExactAsync(
        QuicStream stream,
        int length,
        CancellationToken cancellationToken,
        bool allowEnd = false)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return allowEnd && offset == 0
                    ? []
                    : throw new IOException("Connection closed while reading HTTP/3 data.");
            }

            offset += read;
        }

        return buffer;
    }

    private readonly record struct Http3FrameReadResult(
        bool EndStream,
        long Type,
        ReadOnlyMemory<byte> Payload)
    {
        public static Http3FrameReadResult End { get; } = new(true, 0, ReadOnlyMemory<byte>.Empty);
    }

    private readonly record struct Http3VarIntReadResult(bool Success, long Value)
    {
        public static Http3VarIntReadResult Failure { get; } = new(false, 0);
    }
}
