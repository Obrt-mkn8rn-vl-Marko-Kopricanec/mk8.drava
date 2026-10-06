namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeCircuitBreakerProjection
{
    public RuntimeCircuitBreakerProjection(bool Enabled, int FailureThreshold, TimeSpan SamplingWindow, TimeSpan OpenDuration, int HalfOpenMaxAttempts, IReadOnlyList<int> FailureStatusCodes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(FailureThreshold);
        if (SamplingWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(SamplingWindow));
        }

        if (OpenDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(OpenDuration));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(HalfOpenMaxAttempts);
        this.Enabled = Enabled;
        this.FailureThreshold = FailureThreshold;
        this.SamplingWindow = SamplingWindow;
        this.OpenDuration = OpenDuration;
        this.HalfOpenMaxAttempts = HalfOpenMaxAttempts;
        this.FailureStatusCodes = RuntimeList.Copy(FailureStatusCodes);
    }

    public bool Enabled { get; }
    public int FailureThreshold { get; }
    public TimeSpan SamplingWindow { get; }
    public TimeSpan OpenDuration { get; }
    public int HalfOpenMaxAttempts { get; }
    public IReadOnlyList<int> FailureStatusCodes { get; }
}
