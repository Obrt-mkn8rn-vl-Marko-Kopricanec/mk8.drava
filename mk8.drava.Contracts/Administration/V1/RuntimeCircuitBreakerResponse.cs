namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeCircuitBreakerResponse
{
    public RuntimeCircuitBreakerResponse(bool enabled, int failureThreshold, TimeSpan samplingWindow, TimeSpan openDuration, int halfOpenMaxAttempts, IReadOnlyList<int> failureStatusCodes)
    {
        Enabled = enabled;
        FailureThreshold = failureThreshold;
        SamplingWindow = samplingWindow;
        OpenDuration = openDuration;
        HalfOpenMaxAttempts = halfOpenMaxAttempts;
        FailureStatusCodes = ApiResponseList.Copy(failureStatusCodes);
    }

    public bool Enabled { get; }
    public int FailureThreshold { get; }
    public TimeSpan SamplingWindow { get; }
    public TimeSpan OpenDuration { get; }
    public int HalfOpenMaxAttempts { get; }
    public IReadOnlyList<int> FailureStatusCodes { get; }
}
