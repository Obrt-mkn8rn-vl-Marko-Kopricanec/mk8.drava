namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyCircuitBreakerOptions
{
    public bool Enabled { get; init; }
    public int FailureThreshold { get; init; } = 5;
    public int SamplingWindowSeconds { get; init; } = 60;
    public int OpenDurationSeconds { get; init; } = 30;
    public int HalfOpenMaxAttempts { get; init; } = 1;
    public IList<int> FailureStatusCodes { get; init; } = [];
}
