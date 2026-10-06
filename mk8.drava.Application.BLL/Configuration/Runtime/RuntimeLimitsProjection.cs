namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeLimitsProjection
{
    public RuntimeLimitsProjection(int MaxActiveClientConnections, int MaxConcurrentTlsHandshakes, int RequestsPerMinutePerIp, int UpgradeRequestsPerMinutePerIp, int MaxRequestHeadBytes, int MaxHeaderCount, int MaxHeaderLineBytes, long MaxRequestBodyBytes, int MaxPathBytes, TimeSpan ShutdownGracePeriod)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxActiveClientConnections);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxConcurrentTlsHandshakes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RequestsPerMinutePerIp);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(UpgradeRequestsPerMinutePerIp);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxRequestHeadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxHeaderCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxHeaderLineBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRequestBodyBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPathBytes);
        if (ShutdownGracePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ShutdownGracePeriod));
        }

        this.MaxActiveClientConnections = MaxActiveClientConnections;
        this.MaxConcurrentTlsHandshakes = MaxConcurrentTlsHandshakes;
        this.RequestsPerMinutePerIp = RequestsPerMinutePerIp;
        this.UpgradeRequestsPerMinutePerIp = UpgradeRequestsPerMinutePerIp;
        this.MaxRequestHeadBytes = MaxRequestHeadBytes;
        this.MaxHeaderCount = MaxHeaderCount;
        this.MaxHeaderLineBytes = MaxHeaderLineBytes;
        this.MaxRequestBodyBytes = MaxRequestBodyBytes;
        this.MaxPathBytes = MaxPathBytes;
        this.ShutdownGracePeriod = ShutdownGracePeriod;
    }

    public int MaxActiveClientConnections { get; }
    public int MaxConcurrentTlsHandshakes { get; }
    public int RequestsPerMinutePerIp { get; }
    public int UpgradeRequestsPerMinutePerIp { get; }
    public int MaxRequestHeadBytes { get; }
    public int MaxHeaderCount { get; }
    public int MaxHeaderLineBytes { get; }
    public long MaxRequestBodyBytes { get; }
    public int MaxPathBytes { get; }
    public TimeSpan ShutdownGracePeriod { get; }
}
