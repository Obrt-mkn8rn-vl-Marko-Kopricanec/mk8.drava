namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyLimitsOptions
{
    public int MaxActiveClientConnections { get; init; } = 4096;
    public int MaxConcurrentTlsHandshakes { get; init; } = 128;
    public int RequestsPerMinutePerIp { get; init; } = 240;
    public int UpgradeRequestsPerMinutePerIp { get; init; } = 30;
    public int MaxRequestHeadBytes { get; init; } = 32 * 1024;
    public int MaxHeaderCount { get; init; } = 128;
    public int MaxHeaderLineBytes { get; init; } = 8192;
    public long MaxRequestBodyBytes { get; init; } = 100L * 1024 * 1024;
    public int MaxPathBytes { get; init; } = 8192;
    public int ShutdownGracePeriodSeconds { get; init; } = 15;
}
