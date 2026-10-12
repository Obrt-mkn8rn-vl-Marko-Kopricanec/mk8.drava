using Grpc.Core;
using System.Diagnostics;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Transport.Streaming;

public sealed class ExchangeClientWriter(IClientStreamWriter<ExchangeFrame> writer, int window, CancellationToken exchangeCancellation) : IClientStreamWriter<ExchangeFrame>, IDisposable
{
    private readonly SemaphoreSlim _serialization = new(1, 1);
    private bool _completed;
    internal ExchangeWriteObservation? Observation { get; set; }
    public int WindowFrames { get; } = window;
    public FrameWindow RequestWindow { get; } = new(window);
    public WriteOptions? WriteOptions { get => writer.WriteOptions; set => writer.WriteOptions = value; }
    public Task WriteAsync(ExchangeFrame message) => WriteAsync(message, exchangeCancellation);

    public async Task WriteAsync(ExchangeFrame message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var observed = message.FrameCase == ExchangeFrame.FrameOneofCase.Data ? Observation : null;
        var started = observed is not null ? Stopwatch.GetTimestamp() : 0;
        if (message.FrameCase == ExchangeFrame.FrameOneofCase.Data)
        {
            try { await RequestWindow.TakeAsync(cancellationToken).ConfigureAwait(false); }
            finally { observed?.CreditWaited(Stopwatch.GetTimestamp() - started); }
        }
        started = observed is not null ? Stopwatch.GetTimestamp() : 0;
        try { await _serialization.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally { observed?.SerializationWaited(Stopwatch.GetTimestamp() - started); }
        try
        {
            if (_completed) throw new InvalidOperationException("Private request stream is already complete.");
            started = observed is not null ? Stopwatch.GetTimestamp() : 0;
            try { await writer.WriteAsync(message, exchangeCancellation).ConfigureAwait(false); }
            finally { observed?.TransportWritten(Stopwatch.GetTimestamp() - started); }
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
