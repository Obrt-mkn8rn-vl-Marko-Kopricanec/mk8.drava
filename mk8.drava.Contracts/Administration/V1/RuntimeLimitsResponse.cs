namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeLimitsResponse(int MaxActiveClientConnections, int MaxConcurrentTlsHandshakes, int RequestsPerMinutePerIp, int UpgradeRequestsPerMinutePerIp, int MaxRequestHeadBytes, int MaxHeaderCount, int MaxHeaderLineBytes, long MaxRequestBodyBytes, int MaxPathBytes, TimeSpan ShutdownGracePeriod)
{
}
