namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyLimitSubsystemSummaryResponse(int MaxActiveClientConnections, long ActiveConnections, int MaxConcurrentTlsHandshakes, long ActiveTlsHandshakes, long ActiveHttp2Streams, long ActiveHttp3Streams, long ActiveUpstreamHttp3Streams, int RequestsPerMinutePerIp)
{
}
