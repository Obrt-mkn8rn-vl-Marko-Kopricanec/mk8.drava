namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeRetryPolicyResponse
{
    public RuntimeRetryPolicyResponse(bool enabled, int maxAttempts, TimeSpan? perAttemptTimeout, bool retryOnConnectFailure, bool retryOnUpstreamResponseHeadTimeout, IReadOnlyList<int> retryOnStatusCodes, IReadOnlyList<string> retryMethods, TimeSpan retryBackoff)
    {
        Enabled = enabled;
        MaxAttempts = maxAttempts;
        PerAttemptTimeout = perAttemptTimeout;
        RetryOnConnectFailure = retryOnConnectFailure;
        RetryOnUpstreamResponseHeadTimeout = retryOnUpstreamResponseHeadTimeout;
        RetryOnStatusCodes = ApiResponseList.Copy(retryOnStatusCodes);
        RetryMethods = ApiResponseList.Copy(retryMethods);
        RetryBackoff = retryBackoff;
    }

    public bool Enabled { get; }
    public int MaxAttempts { get; }
    public TimeSpan? PerAttemptTimeout { get; }
    public bool RetryOnConnectFailure { get; }
    public bool RetryOnUpstreamResponseHeadTimeout { get; }
    public IReadOnlyList<int> RetryOnStatusCodes { get; }
    public IReadOnlyList<string> RetryMethods { get; }
    public TimeSpan RetryBackoff { get; }
}
