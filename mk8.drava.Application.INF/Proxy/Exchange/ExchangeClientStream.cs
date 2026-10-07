using Grpc.Core;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Proxy.Exchange;

public sealed class ExchangeClientStream : Stream
{
    private readonly IServerStreamWriter<ExchangeFrame> _writer;
    private readonly ExchangeRequestBody _request;
    private readonly ExchangeResponseWriter _response;
    private bool _disposed;
    private bool _uploadStopSent;
    public string Method { get; }

    public ExchangeClientStream(IAsyncStreamReader<ExchangeFrame> reader, IServerStreamWriter<ExchangeFrame> writer, RequestHead head, Func<CancellationToken, ValueTask>? consumed = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(head);
        _writer = writer;
        Method = head.Method;
        _request = new ExchangeRequestBody(reader, head, consumed);
        _response = new ExchangeResponseWriter(writer, head.Method, StopUploadAsync, head.WantsUpgrade, AcceptUpgradeAsync);
    }

    private ValueTask AcceptUpgradeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _request.AcceptUpgrade();
        return ValueTask.CompletedTask;
    }

    public bool ResponseStarted => _response.ResponseStarted;
    public bool UploadCompleted => _request.Completed;
    public void SetResponseTrailers(IReadOnlyList<ProxyHeaderField> fields)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _response.SetTrailers(fields);
    }
    public override bool CanRead => !_disposed;
    public override bool CanWrite => !_disposed;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _request.ReadAsync(buffer, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _response.WriteAsync(buffer, cancellationToken);
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        await StopUploadAsync(cancellationToken).ConfigureAwait(false);
        await _request.RequireCompletionAsync(cancellationToken).ConfigureAwait(false);
        await _response.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask VerifyEmptyUploadAsync(CancellationToken cancellationToken) => _request.RequireCompletionAsync(cancellationToken);

    public async ValueTask AllowUploadAsync(CancellationToken cancellationToken)
    {
        if (!_request.Completed)
            await _writer.WriteAsync(new ExchangeFrame { UploadAllowed = new UploadAllowed() }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopUploadAsync(CancellationToken cancellationToken)
    {
        if (_request.Completed || _uploadStopSent) return;
        _uploadStopSent = true;
        _request.RequestStop();
        await _writer.WriteAsync(new ExchangeFrame { StopUpload = new StopUpload() }, cancellationToken).ConfigureAwait(false);
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("The exchange is asynchronous.");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("The exchange is asynchronous.");
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) { _request.Dispose(); _response.Dispose(); _disposed = true; }
        base.Dispose(disposing);
    }
}
