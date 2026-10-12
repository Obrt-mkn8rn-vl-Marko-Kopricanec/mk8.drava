namespace Mk8.Drava.Transport.Streaming;

// One upload owns these counters; atomic reads also permit a cancellation snapshot.
internal sealed class ExchangeWriteObservation
{
    private long _creditTicks;
    private long _serializationTicks;
    private long _writeTicks;
    internal long CreditTicks => Interlocked.Read(ref _creditTicks);
    internal long SerializationTicks => Interlocked.Read(ref _serializationTicks);
    internal long WriteTicks => Interlocked.Read(ref _writeTicks);
    internal void CreditWaited(long ticks) => Interlocked.Add(ref _creditTicks, ticks);
    internal void SerializationWaited(long ticks) => Interlocked.Add(ref _serializationTicks, ticks);
    internal void TransportWritten(long ticks) => Interlocked.Add(ref _writeTicks, ticks);
}
