using System.Text;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.INF.Proxy.Exchange;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;

public sealed partial class ProxyForwarder
{
    private static readonly UTF8Encoding TrailerEncoding = new(false, true);

    private static async ValueTask FinishFramedRequestAsync(Http1BodyReader reader, SendFramedUpstreamDataAsync sendData,
        SendFramedUpstreamTrailersAsync? sendTrailers, int maximumLineBytes, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        if (sendTrailers is null)
            await DiscardTrailerSectionAsync(reader, maximumLineBytes, cancellationToken).ConfigureAwait(false);
        else
        {
            var fields = await ReadFramedRequestTrailersAsync(reader, maximumLineBytes, cancellationToken).ConfigureAwait(false);
            if (fields.Count > 0)
            {
                await sendTrailers(fields, timeouts, cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        await sendData(ReadOnlyMemory<byte>.Empty, true, timeouts, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<IReadOnlyList<ProxyHeaderField>> ReadFramedRequestTrailersAsync(Http1BodyReader reader,
        int maximumLineBytes, CancellationToken cancellationToken)
    {
        List<ProxyHeaderField> fields = [];
        List<Header> typed = [];
        var bytes = 0;
        while (true)
        {
            var line = await reader.ReadLineWithCrlfAsync(maximumLineBytes, cancellationToken).ConfigureAwait(false);
            if (line.Length == 2) break;
            bytes = checked(bytes + line.Length);
            if (bytes > FrameLimits.MaximumHeaderBytes || fields.Count == FrameLimits.MaximumHeaderCount)
                throw new Http1ClientProtocolException("Request trailing fields exceeded their bound.");
            string text;
            try { text = TrailerEncoding.GetString(line.AsSpan(0, line.Length - 2)); }
            catch (DecoderFallbackException exception) { throw new Http1ClientProtocolException("Invalid encoded request trailing fields.", exception); }
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) throw new Http1ClientProtocolException("Invalid request trailing field.");
            var name = text[..colon];
            var value = text[(colon + 1)..].Trim(' ', '\t');
            if (HopByHopHeaderPolicy.IsHopByHopHeader(name)) throw new Http1ClientProtocolException("Forbidden request trailing field.");
            fields.Add(new ProxyHeaderField(name, value));
            typed.Add(new Header { Name = name, Value = value });
        }
        try { FrameLimits.ValidateHeaders(typed, trailers: true); }
        catch (InvalidDataException exception) { throw new Http1ClientProtocolException("Forbidden or malformed request trailing fields.", exception); }
        return fields;
    }

    private async ValueTask WriteFramedResponseEndingAsync(Stream destination, IReadOnlyList<ProxyHeaderField>? fields,
        RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        var text = new StringBuilder("0\r\n");
        if (destination is not ExchangeClientStream && fields is not null)
            foreach (var field in fields) text.Append(field.Name).Append(": ").Append(field.Value).Append("\r\n");
        text.Append("\r\n");
        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        await ProxyTimedStreamWriter.WriteAsync(destination, bytes, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
        _metrics.AddBytesWritten(bytes.Length);
    }

    private async ValueTask WriteBufferedFramedTrailersAsync(Stream destination, Http1ResponseHead head,
        IReadOnlyList<ProxyHeaderField> headers, BufferedFramedBody body, RuntimeListener listener, RuntimeTimeouts timeouts,
        string requestId, bool keepAlive, Action markResponseStarted, CancellationToken cancellationToken)
    {
        if (destination is ExchangeClientStream exchange)
        {
            await WriteBufferedResponseAsync(destination, head, headers, body.Data, keepAlive, requestId, listener, timeouts, cancellationToken).ConfigureAwait(false);
            markResponseStarted();
            exchange.SetResponseTrailers(body.Trailers ?? []);
            return;
        }
        var chunked = new Http1ResponseHead(head.Version, head.StatusCode, head.ReasonPhrase, Http1ResponseFraming.Chunked, head.Headers);
        await WriteResponseHeadAsync(destination, chunked, headers, timeouts, keepAlive, requestId, listener, cancellationToken).ConfigureAwait(false);
        markResponseStarted();
        if (body.Data.Length > 0) await WriteHttp1ChunkAsync(destination, body.Data, timeouts.DownstreamWriteTimeout, cancellationToken).ConfigureAwait(false);
        await WriteFramedResponseEndingAsync(destination, body.Trailers, timeouts, cancellationToken).ConfigureAwait(false);
    }
}
