using System.Globalization;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Proxy.Exchange;

// A bounded incremental decoder for MDRAVA's virtual downstream HTTP/1 writes.
public sealed class ExchangeResponseWriter : IDisposable
{
    private readonly IServerStreamWriter<ExchangeFrame> _writer;
    private readonly string _method;
    private readonly Func<CancellationToken, ValueTask> _stopUpload;
    private readonly byte[] _metadata = new byte[FrameLimits.MaximumHeaderBytes];
    private readonly BodyDigest _digest = new();
    private readonly List<Header> _trailers = [];
    private int _metadataLength;
    private int _trailerBytes;
    private int _informationalCount;
    private long _remaining;
    private DecodeState _state;
    private bool _finalHead;
    private bool _finished;

    public ExchangeResponseWriter(IServerStreamWriter<ExchangeFrame> writer, string method, Func<CancellationToken, ValueTask> stopUpload)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(stopUpload);
        _writer = writer;
        _method = method;
        _stopUpload = stopUpload;
    }

    public bool ResponseStarted => _finalHead;

    public void SetTrailers(IReadOnlyList<ProxyHeaderField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (!_finalHead || _finished || _state is not DecodeState.Done and not DecodeState.CloseBody || _trailers.Count > 0)
            throw new InvalidDataException("Response trailing fields require a complete body and a single trailer section.");
        List<Header> typed = [];
        foreach (var field in fields) typed.Add(new Header { Name = field.Name, Value = field.Value });
        FrameLimits.ValidateHeaders(typed, trailers: true);
        _trailers.AddRange(typed);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (_finished) throw new InvalidOperationException("Response is already complete.");
        while (!data.IsEmpty)
        {
            if (_state is DecodeState.Head or DecodeState.ChunkLine or DecodeState.ChunkEnd or DecodeState.Trailers)
            {
                var value = data.Span[0];
                data = data[1..];
                await MetadataByteAsync(value, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (_state == DecodeState.Done) throw new InvalidDataException("Bytes follow a framed response.");
            var count = Math.Min(data.Length, FrameLimits.MaximumFrameBytes);
            if (_state is DecodeState.FixedBody or DecodeState.ChunkBody) count = (int)Math.Min(count, _remaining);
            if (count <= 0) throw new InvalidDataException("Invalid response decoder state.");
            await DataAsync(data[..count], cancellationToken).ConfigureAwait(false);
            data = data[count..];
            if (_state is DecodeState.FixedBody or DecodeState.ChunkBody)
            {
                _remaining -= count;
                if (_remaining == 0) _state = _state == DecodeState.FixedBody ? DecodeState.Done : DecodeState.ChunkEnd;
            }
        }
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        if (_finished || !_finalHead || _state is not DecodeState.Done and not DecodeState.CloseBody and not DecodeState.Upgrade)
            throw new InvalidDataException("Forwarding ended before a complete response.");
        if (_trailers.Count > 0)
        {
            var trailers = new TrailerFrame();
            trailers.Headers.Add(_trailers);
            FrameLimits.ValidateHeaders(trailers.Headers, trailers: true);
            await _writer.WriteAsync(new ExchangeFrame { Trailers = trailers }, cancellationToken).ConfigureAwait(false);
        }
        await _writer.WriteAsync(new ExchangeFrame { Complete = _digest.Complete() }, cancellationToken).ConfigureAwait(false);
        _finished = true;
    }

    private async ValueTask MetadataByteAsync(byte value, CancellationToken cancellationToken)
    {
        var limit = _state == DecodeState.Head ? FrameLimits.MaximumHeaderBytes : 8192;
        if (_metadataLength == limit) throw new InvalidDataException("Response metadata exceeds its bound.");
        _metadata[_metadataLength++] = value;
        if (_state == DecodeState.Head)
        {
            if (_metadataLength >= 4 && _metadata.AsSpan(_metadataLength - 4, 4).SequenceEqual("\r\n\r\n"u8))
                await HeadAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        if (_metadataLength < 2 || _metadata[_metadataLength - 2] != '\r' || value != '\n') return;
        var line = Encoding.ASCII.GetString(_metadata, 0, _metadataLength - 2);
        _metadataLength = 0;
        switch (_state)
        {
            case DecodeState.ChunkLine:
                var separator = line.IndexOf(';', StringComparison.Ordinal);
                var size = separator < 0 ? line.AsSpan() : line.AsSpan(0, separator);
                if (!long.TryParse(size, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _remaining) || _remaining < 0)
                    throw new InvalidDataException("Invalid response chunk size.");
                _state = _remaining == 0 ? DecodeState.Trailers : DecodeState.ChunkBody;
                break;
            case DecodeState.ChunkEnd:
                if (line.Length != 0) throw new InvalidDataException("Invalid response chunk terminator.");
                _state = DecodeState.ChunkLine;
                break;
            case DecodeState.Trailers:
                AddTrailer(line);
                break;
            default:
                throw new InvalidOperationException("Invalid metadata state.");
        }
    }

    private async ValueTask HeadAsync(CancellationToken cancellationToken)
    {
        if (!Http1ResponseParser.TryParse(_metadata.AsSpan(0, _metadataLength), _method, out var parsed, out _))
            throw new InvalidDataException("MDRAVA emitted an invalid response head.");
        _metadataLength = 0;
        var head = new ResponseHead { StatusCode = (uint)parsed.StatusCode, Informational = Http1ResponseParser.IsInformational(parsed), Upgrade = parsed.StatusCode == 101 };
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "connection", "keep-alive", "proxy-connection", "transfer-encoding" };
        foreach (var field in parsed.Headers)
            if (string.Equals(field.Name, "connection", StringComparison.OrdinalIgnoreCase))
                foreach (var token in field.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (!head.Upgrade || !string.Equals(token, "upgrade", StringComparison.OrdinalIgnoreCase)) removed.Add(token);
        foreach (var field in parsed.Headers)
            if (!removed.Contains(field.Name)) head.Headers.Add(new Header { Name = field.Name, Value = field.Value });
        FrameLimits.ValidateResponse(head);
        if (head.Informational)
        {
            if (++_informationalCount > 8) throw new InvalidDataException("Too many informational responses.");
            await _writer.WriteAsync(new ExchangeFrame { Response = head }, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (_finalHead) throw new InvalidDataException("Multiple final response heads.");
        _finalHead = true;
        if (!head.Upgrade && (head.StatusCode >= 300 || string.Equals(_method, "HEAD", StringComparison.OrdinalIgnoreCase)))
            await _stopUpload(cancellationToken).ConfigureAwait(false);
        _state = head.Upgrade ? DecodeState.Upgrade : parsed.Framing.Kind switch
        {
            Http1BodyKind.None => DecodeState.Done,
            Http1BodyKind.ContentLength => DecodeState.FixedBody,
            Http1BodyKind.Chunked => DecodeState.ChunkLine,
            Http1BodyKind.CloseDelimited => DecodeState.CloseBody,
            _ => throw new InvalidDataException("Unsupported response framing.")
        };
        _remaining = parsed.Framing.ContentLength.GetValueOrDefault();
        await _writer.WriteAsync(new ExchangeFrame { Response = head }, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask DataAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        _digest.Append(payload.Span);
        await _writer.WriteAsync(new ExchangeFrame { Data = new DataFrame { Payload = ByteString.CopyFrom(payload.Span) } }, cancellationToken).ConfigureAwait(false);
    }

    private void AddTrailer(string line)
    {
        if (line.Length == 0) { _state = DecodeState.Done; return; }
        _trailerBytes = checked(_trailerBytes + line.Length + 2);
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || _trailerBytes > FrameLimits.MaximumHeaderBytes || _trailers.Count == FrameLimits.MaximumHeaderCount)
            throw new InvalidDataException("Invalid or oversized response trailers.");
        _trailers.Add(new Header { Name = line[..colon], Value = line[(colon + 1)..].Trim(' ', '\t') });
    }

    public void Dispose() => _digest.Dispose();

    private enum DecodeState { Head, FixedBody, ChunkLine, ChunkBody, ChunkEnd, Trailers, CloseBody, Upgrade, Done }
}
