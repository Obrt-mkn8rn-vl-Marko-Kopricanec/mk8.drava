namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeRetryProjection
{
    public RuntimeRetryProjection(bool Enabled, int MaxAttempts, TimeSpan? PerAttemptTimeout, bool RetryOnConnectFailure, bool RetryOnUpstreamResponseHeadTimeout, IEnumerable<int> RetryOnStatusCodes, IEnumerable<string> RetryMethods, TimeSpan RetryBackoff)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxAttempts);
        if (PerAttemptTimeout is { } perAttemptTimeout && perAttemptTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PerAttemptTimeout));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(RetryBackoff, TimeSpan.Zero);

        this.Enabled = Enabled;
        this.MaxAttempts = MaxAttempts;
        this.PerAttemptTimeout = PerAttemptTimeout;
        this.RetryOnConnectFailure = RetryOnConnectFailure;
        this.RetryOnUpstreamResponseHeadTimeout = RetryOnUpstreamResponseHeadTimeout;
        this.RetryOnStatusCodes = RuntimeList.Copy(RetryOnStatusCodes);
        this.RetryMethods = RuntimeList.Copy(RetryMethods);
        this.RetryBackoff = RetryBackoff;
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
