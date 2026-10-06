namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyRetryPolicyOptions
{
    public bool Enabled { get; init; }
    public int MaxAttempts { get; init; } = 1;
    public int? PerAttemptTimeoutMs { get; init; }
    public bool RetryOnConnectFailure { get; init; }
    public bool RetryOnUpstreamResponseHeadTimeout { get; init; }
    public System.Collections.ObjectModel.Collection<int> RetryOnStatusCodes { get; init; } = [];
    public System.Collections.ObjectModel.Collection<string> RetryMethods { get; init; } = ["GET", "HEAD"];
    public int RetryBackoffMilliseconds { get; init; }
}
