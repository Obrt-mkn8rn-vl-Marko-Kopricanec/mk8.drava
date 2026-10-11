using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.Http;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;

public sealed partial class ProxyForwarder
{
    private static long? Http1UpstreamContentLength(Http1RequestHead head)
    {
        // An explicitly declared trailer section requires chunked HTTP/1 framing.
        if (head.SourceContentLength.HasValue && !head.Headers.Any(static field => string.Equals(field.Name, "Trailer", StringComparison.OrdinalIgnoreCase)))
            return head.SourceContentLength;
        return head.Framing.ContentLength;
    }

    private async ValueTask WriteHttp1PayloadAsync(Stream upstream, ReadOnlyMemory<byte> data, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        await ProxyTimedStreamWriter.WriteAsync(upstream, data, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
        _metrics.AddBytesWritten(data.Length);
    }

    private async ValueTask RelayKnownHttp1PayloadAsync(Http1BodyReader reader, Stream upstream, Http1RequestHead head, RuntimeListener listener,
        RuntimeTimeouts timeouts, byte[]? initialChunkLine, long maximumBytes, CancellationToken cancellationToken)
    {
        var observe = _logger.IsEnabled(LogLevel.Debug);
        var started = observe ? Stopwatch.GetTimestamp() : 0;
        long writes = 0, bytes = 0, smallWrites = 0, writeTicks = 0;
        var maximumWrite = 0;
        var completed = false;
        async ValueTask SendAsync(ReadOnlyMemory<byte> data, bool _, RuntimeTimeouts policy, CancellationToken token)
        {
            var writeStarted = observe ? Stopwatch.GetTimestamp() : 0;
            try { await WriteHttp1PayloadAsync(upstream, data, policy, token).ConfigureAwait(false); }
            finally { if (observe) writeTicks += Stopwatch.GetTimestamp() - writeStarted; }
            if (!observe) return;
            writes++;
            bytes += data.Length;
            if (data.Length is > 0 and < 16 * 1024) smallWrites++;
            maximumWrite = Math.Max(maximumWrite, data.Length);
        }
        try
        {
            await RelayChunkedBodyToFramedUpstreamAsync(reader, SendAsync, listener, timeouts, initialChunkLine, maximumBytes,
                RejectFixedHttp1TrailersAsync, cancellationToken).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            if (observe)
            {
                LogKnownUploadCounters(_logger, head.SourceContentLength, bytes, writes, smallWrites, maximumWrite, completed);
                LogKnownUploadTiming(_logger, 1000d * (Stopwatch.GetTimestamp() - started) / Stopwatch.Frequency,
                    1000d * writeTicks / Stopwatch.Frequency);
            }
        }
    }

    private static ValueTask RejectFixedHttp1TrailersAsync(IReadOnlyList<ProxyHeaderField> fields, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (fields.Count != 0)
            throw new Http1ClientProtocolException("Undeclared trailing fields cannot be represented in a fixed-length HTTP/1 request.");
        return ValueTask.CompletedTask;
    }
}
