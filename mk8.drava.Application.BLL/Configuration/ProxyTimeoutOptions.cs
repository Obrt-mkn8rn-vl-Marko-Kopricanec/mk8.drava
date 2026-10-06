namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyTimeoutOptions
{
    public int ClientRequestHeadTimeoutMs { get; init; } = 10_000;
    public int ClientRequestBodyIdleTimeoutMs { get; init; } = 30_000;
    public int UpstreamConnectTimeoutMs { get; init; } = 5_000;
    public int UpstreamResponseHeadTimeoutMs { get; init; } = 30_000;
    public int UpstreamResponseBodyIdleTimeoutMs { get; init; } = 30_000;
    public int DownstreamWriteTimeoutMs { get; init; } = 30_000;
    public int TlsHandshakeTimeoutMs { get; init; } = 10_000;
    public int ClientKeepAliveIdleTimeoutMs { get; init; } = 30_000;
    public int UpstreamIdleConnectionLifetimeMs { get; init; } = 60_000;
    public int TunnelIdleTimeoutMs { get; init; } = 60_000;
}
