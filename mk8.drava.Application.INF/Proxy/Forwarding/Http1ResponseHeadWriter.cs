using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.Http;
using System.Globalization;
using System.Text;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;
internal static class Http1ResponseHeadWriter
{
    public static async ValueTask WriteAsync(Stream destination, Http1ResponseHead responseHead, IReadOnlyList<ProxyHeaderField> responseHeaders, IReadOnlyList<ProxyHeaderField> additionalHeaders, string requestId, long? contentLength, bool useChunkedTransferEncoding, bool keepClientConnectionOpen, TimeSpan writeTimeout, ProxyMetrics metrics, CancellationToken cancellationToken)
    {
        if (contentLength.HasValue && useChunkedTransferEncoding)
        {
            throw new ArgumentException("HTTP/1 response head cannot advertise both Content-Length and chunked transfer coding.");
        }

        var builder = new StringBuilder();
        builder.Append(responseHead.Version).Append(' ').Append(responseHead.StatusCode).Append(' ').Append(responseHead.ReasonPhrase).Append("\r\n");
        AppendHeaders(builder, responseHeaders);
        AppendHeaders(builder, additionalHeaders);
        builder.Append("X-Request-Id: ").Append(requestId).Append("\r\n");
        if (contentLength.HasValue)
        {
            builder.Append("Content-Length: ").Append(contentLength.GetValueOrDefault().ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }
        else if (useChunkedTransferEncoding)
        {
            builder.Append("Transfer-Encoding: chunked\r\n");
        }

        builder.Append(keepClientConnectionOpen ? "Connection: keep-alive\r\n\r\n" : "Connection: close\r\n\r\n");
        var bytes = Encoding.ASCII.GetBytes(builder.ToString());
        await ProxyTimedStreamWriter.WriteAsync(destination, bytes, writeTimeout, cancellationToken).ConfigureAwait(false);
        metrics.AddBytesWritten(bytes.Length);
    }

    private static void AppendHeaders(StringBuilder builder, IReadOnlyList<ProxyHeaderField> headers)
    {
        foreach (var header in headers)
        {
            if (Http1ManagedHeaderPolicy.IsManagedFramingHeader(header.Name))
            {
                continue;
            }

            builder.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        }
    }
}
