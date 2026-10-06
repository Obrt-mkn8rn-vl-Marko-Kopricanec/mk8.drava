using Grpc.Core;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Transport.Streaming;

public sealed class ExchangeServerWriter(IServerStreamWriter<ExchangeFrame> writer, int window, CancellationToken exchangeCancellation) : IServerStreamWriter<ExchangeFrame>, IDisposable
{
    private readonly SemaphoreSlim _serialization = new(1, 1);
    public FrameWindow ResponseWindow { get; } = new(window);
    public WriteOptions? WriteOptions { get => writer.WriteOptions; set => writer.WriteOptions = value; }

    public Task WriteAsync(ExchangeFrame message) => WriteAsync(message, exchangeCancellation);

    public async Task WriteAsync(ExchangeFrame message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.FrameCase == ExchangeFrame.FrameOneofCase.Data) await ResponseWindow.TakeAsync(cancellationToken).ConfigureAwait(false);
        await _serialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await writer.WriteAsync(message, exchangeCancellation).ConfigureAwait(false); }
        finally { _serialization.Release(); }
    }

    public void Dispose() { ResponseWindow.Dispose(); _serialization.Dispose(); }
}
