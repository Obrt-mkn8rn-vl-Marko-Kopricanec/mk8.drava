namespace Mk8.Drava.UnitTests;

internal sealed class RegistryTimeProvider : TimeProvider
{
    private long _timestamp;
    private long _utcTicks = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero).Ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
    public void Advance(TimeSpan duration)
    {
        Interlocked.Add(ref _timestamp, duration.Ticks);
        Interlocked.Add(ref _utcTicks, duration.Ticks);
    }
    public void AdjustUtc(TimeSpan duration) => Interlocked.Add(ref _utcTicks, duration.Ticks);
}
