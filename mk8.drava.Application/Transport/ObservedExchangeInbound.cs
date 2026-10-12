using System.Diagnostics;
using Grpc.Core;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Streaming;

namespace Mk8.Drava.Application.Transport;

// Aggregate phase observations retain the original bounded reader and credit writer.
internal sealed partial class ObservedExchangeInbound(ExchangeInbound inbound, ILogger logger) : IAsyncStreamReader<ExchangeFrame>
{
    private readonly bool _enabled = logger.IsEnabled(LogLevel.Debug);
    private long _reads;
    private long _readTicks;
    private long _credits;
    private long _creditTicks;
    public ExchangeFrame Current => inbound.Current;

    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        var started = _enabled ? Stopwatch.GetTimestamp() : 0;
        try { return await inbound.MoveNext(cancellationToken).ConfigureAwait(false); }
        finally
        {
            if (_enabled) { _reads++; _readTicks += Stopwatch.GetTimestamp() - started; }
        }
    }

    internal async ValueTask RequestConsumedAsync(CancellationToken cancellationToken)
    {
        var started = _enabled ? Stopwatch.GetTimestamp() : 0;
        try { await inbound.RequestConsumedAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            if (_enabled) { _credits++; _creditTicks += Stopwatch.GetTimestamp() - started; }
        }
    }

    internal void Report()
    {
        if (_enabled) LogPhases(logger, _reads, _credits, 1000d * _readTicks / Stopwatch.Frequency, 1000d * _creditTicks / Stopwatch.Frequency);
    }

    [LoggerMessage(10072, LogLevel.Debug, "Exchange upload phases readCalls={ReadCalls} creditCalls={CreditCalls} queuedReadMilliseconds={QueuedReadMilliseconds} creditWriteMilliseconds={CreditWriteMilliseconds}")]
    private static partial void LogPhases(ILogger logger, long readCalls, long creditCalls, double queuedReadMilliseconds, double creditWriteMilliseconds);
}
