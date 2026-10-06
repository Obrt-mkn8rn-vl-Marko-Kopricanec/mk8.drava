namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeHealthCheckFacts
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromSeconds(3600);
    private static readonly TimeSpan MinimumTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromSeconds(300);
    public static void Validate(string path, TimeSpan interval, TimeSpan timeout, int healthyThreshold, int unhealthyThreshold)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.StartsWith('/'))
        {
            throw new ArgumentException("Health check path must start with '/'.", nameof(path));
        }

        if (interval < MinimumInterval || interval > MaximumInterval)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        if (timeout < MinimumTimeout || timeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (timeout > interval)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (healthyThreshold is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(healthyThreshold));
        }

        if (unhealthyThreshold is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(unhealthyThreshold));
        }
    }
}
