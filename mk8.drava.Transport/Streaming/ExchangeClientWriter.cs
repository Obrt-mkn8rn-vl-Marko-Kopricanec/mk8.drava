using Grpc.Core;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Transport.Streaming;

public sealed class ExchangeClientWriter(IClientStreamWriter<ExchangeFrame> writer, int window, CancellationToken exchangeCancellation) : IClientStreamWriter<ExchangeFrame>, IDisposable
{
    private readonly SemaphoreSlim _serialization = new(1, 1);
    private bool _completed;
    public FrameWindow RequestWindow { get; } = new(window);
    public WriteOptions? WriteOptions { get => writer.WriteOptions; set => writer.WriteOptions = value; }
    public Task WriteAsync(ExchangeFrame message) => WriteAsync(message, exchangeCancellation);

    public async Task WriteAsync(ExchangeFrame message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.FrameCase == ExchangeFrame.FrameOneofCase.Data) await RequestWindow.TakeAsync(cancellationToken).ConfigureAwait(false);
        await _serialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_completed) throw new InvalidOperationException("Private request stream is already complete.");
            await writer.WriteAsync(message, exchangeCancellation).ConfigureAwait(false);
        }
        finally { _serialization.Release(); }
    }

    public async Task CompleteAsync()
    {
        await _serialization.WaitAsync(exchangeCancellation).ConfigureAwait(false);
        try
        {
            if (_completed) return;
            await writer.CompleteAsync().ConfigureAwait(false);
            _completed = true;
        }
        finally { _serialization.Release(); }
    }

    public void Dispose() { RequestWindow.Dispose(); _serialization.Dispose(); }
}
