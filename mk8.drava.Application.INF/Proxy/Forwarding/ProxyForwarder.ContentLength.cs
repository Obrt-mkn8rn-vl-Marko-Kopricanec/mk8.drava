using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.Http;

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

    private static ValueTask RejectFixedHttp1TrailersAsync(IReadOnlyList<ProxyHeaderField> fields, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (fields.Count != 0)
            throw new Http1ClientProtocolException("Undeclared trailing fields cannot be represented in a fixed-length HTTP/1 request.");
        return ValueTask.CompletedTask;
    }
}
