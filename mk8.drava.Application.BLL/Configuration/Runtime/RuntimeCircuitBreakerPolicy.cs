namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeCircuitBreakerPolicy
{
    public RuntimeCircuitBreakerPolicy(bool Enabled, int FailureThreshold, TimeSpan SamplingWindow, TimeSpan OpenDuration, int HalfOpenMaxAttempts, IReadOnlyList<int> FailureStatusCodes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(FailureThreshold);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(SamplingWindow, TimeSpan.Zero);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(OpenDuration, TimeSpan.Zero);

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
    public static RuntimeCircuitBreakerPolicy Disabled { get; } = new(false, 5, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30), 1, []);
}
