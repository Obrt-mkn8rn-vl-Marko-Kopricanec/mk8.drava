using System.Globalization;
using System.Text;
using Grpc.Core;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Proxy.Exchange;

// At most one data frame and one bounded trailer section are retained. MoveNext supplies backpressure.
public sealed class ExchangeRequestBody : IDisposable
{
    private readonly IAsyncStreamReader<ExchangeFrame> _reader;
    private readonly RequestHead _head;
    private readonly BodyDigest _digest = new();
    private readonly Func<CancellationToken, ValueTask> _consumed;
    private ReadOnlyMemory<byte> _pending;
    private ReadOnlyMemory<byte> _suffix;
    private bool _trailersReceived;
    private bool _wireComplete;
    private bool _stopRequested;
    private bool _creditDue;
    private bool _upgrade;

    public ExchangeRequestBody(IAsyncStreamReader<ExchangeFrame> reader, RequestHead head, Func<CancellationToken, ValueTask>? consumed = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(head);
        _reader = reader;
        _head = head;
        _consumed = consumed ?? (static _ => ValueTask.CompletedTask);
    }

    public bool Completed { get; private set; }
    public bool Stopped { get; private set; }

    public void AcceptUpgrade()
    {
        if (!_head.WantsUpgrade || _head.HasBody || _upgrade || _wireComplete || _stopRequested || !_pending.IsEmpty || !_suffix.IsEmpty)
            throw new InvalidDataException("Request stream cannot enter upgrade mode.");
        _upgrade = true;
    }

    public void RequestStop()
    {
        _stopRequested = true;
    }

    public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        if (destination.IsEmpty) return 0;
        if (_pending.IsEmpty && !_suffix.IsEmpty) { _pending = _suffix; _suffix = default; }
        if (_pending.IsEmpty && !_wireComplete) await NextAsync(cancellationToken).ConfigureAwait(false);
        var count = Math.Min(_pending.Length, destination.Length);
        _pending.Span[..count].CopyTo(destination.Span);
        _pending = _pending[count..];
        return count;
    }

    public async ValueTask RequireCompletionAsync(CancellationToken cancellationToken)
    {
        if (Completed || Stopped) return;
        if (_stopRequested)
        {
            await RequireStoppedAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!_pending.IsEmpty || !_suffix.IsEmpty) throw new InvalidDataException("Request bytes were not consumed.");
        await NextAsync(cancellationToken).ConfigureAwait(false);
        if (!Completed && !Stopped) throw new InvalidDataException("Missing explicit upload completion.");
    }

    private async ValueTask RequireStoppedAsync(CancellationToken cancellationToken)
    {
        _pending = default;
        _suffix = default;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        // Transport windows can contain in-flight frames when a final head stops the source.
        for (var count = 0; count < 64; count++)
        {
            await NextAsync(deadline.Token).ConfigureAwait(false);
            _pending = default;
            _suffix = default;
            if (Completed || Stopped) return;
        }
        throw new InvalidDataException("Upload stop was not acknowledged within its bound.");
    }

    private async ValueTask NextAsync(CancellationToken cancellationToken)
    {
        if (_creditDue)
        {
            await _consumed(cancellationToken).ConfigureAwait(false);
            _creditDue = false;
        }
        if (!await _reader.MoveNext(cancellationToken).ConfigureAwait(false)) throw new InvalidDataException("Upload ended without an explicit completion.");
        var frame = _reader.Current;
        switch (frame.FrameCase)
        {
            case ExchangeFrame.FrameOneofCase.Data:
                AcceptData(frame.Data ?? throw new InvalidDataException("Request data is missing."));
                break;
            case ExchangeFrame.FrameOneofCase.Trailers:
                await AcceptTrailersAsync(frame.Trailers ?? throw new InvalidDataException("Request trailers are missing."), cancellationToken).ConfigureAwait(false);
                break;
            case ExchangeFrame.FrameOneofCase.Complete:
                AcceptCompletion(frame.Complete ?? throw new InvalidDataException("Request completion is missing."), []);
                break;
            case ExchangeFrame.FrameOneofCase.UploadStopped when _stopRequested:
                Stopped = true;
                _wireComplete = true;
                break;
            case ExchangeFrame.FrameOneofCase.Reset:
                throw new IOException("Gateway reset the upload.");
            default:
                throw new InvalidDataException("Unexpected upload frame.");
        }
    }

    private void AcceptData(DataFrame data)
    {
        FrameLimits.ValidateData(data);
        if ((!_head.HasBody && !_upgrade) || _trailersReceived || Completed || Stopped) throw new InvalidDataException("Data outside the upload body.");
        _digest.Append(data.Payload.Span);
        _creditDue = true;
        if (_upgrade) { _pending = data.Payload.Memory; return; }
        if (_head.HasContentLength && _digest.Bytes > (ulong)_head.ContentLength) throw new InvalidDataException("Upload exceeds its declared length.");
        // All public body framing is normalized to a virtual chunked request, including HTTP/2 trailers.
        _pending = Encoding.ASCII.GetBytes(data.Payload.Length.ToString("X", CultureInfo.InvariantCulture) + "\r\n");
        var chunk = new byte[data.Payload.Length + 2];
        data.Payload.Span.CopyTo(chunk);
        chunk[^2] = (byte)'\r';
        chunk[^1] = (byte)'\n';
        _suffix = chunk;
    }

    private async ValueTask AcceptTrailersAsync(TrailerFrame trailers, CancellationToken cancellationToken)
    {
        if (_upgrade || !_head.HasBody || _trailersReceived || Completed) throw new InvalidDataException("Unexpected upload trailers.");
        _trailersReceived = true;
        FrameLimits.ValidateHeaders(trailers.Headers, trailers: true);
        if (!await _reader.MoveNext(cancellationToken).ConfigureAwait(false) || _reader.Current.FrameCase != ExchangeFrame.FrameOneofCase.Complete)
            throw new InvalidDataException("Trailers must be followed by an explicit completion.");
        AcceptCompletion(_reader.Current.Complete ?? throw new InvalidDataException("Request completion is missing."), trailers.Headers);
    }

    private void AcceptCompletion(Completion completion, IEnumerable<Header> trailers)
    {
        if (Completed || Stopped) throw new InvalidDataException("Duplicate upload completion.");
        _digest.Verify(completion);
        if (!_upgrade && _head.HasContentLength && completion.BodyBytes != (ulong)_head.ContentLength) throw new InvalidDataException("Upload differs from its declared length.");
        Completed = true;
        _wireComplete = true;
        if (_upgrade || !_head.HasBody) return;
        var text = new StringBuilder("0\r\n");
        foreach (var field in trailers) text.Append(field.Name).Append(": ").Append(field.Value).Append("\r\n");
        text.Append("\r\n");
        _pending = Encoding.UTF8.GetBytes(text.ToString());
    }

    public void Dispose() => _digest.Dispose();
}
