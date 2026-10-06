namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeTimeoutsProjection
{
    public RuntimeTimeoutsProjection(TimeSpan ClientRequestHeadTimeout, TimeSpan ClientRequestBodyIdleTimeout, TimeSpan UpstreamConnectTimeout, TimeSpan UpstreamResponseHeadTimeout, TimeSpan UpstreamResponseBodyIdleTimeout, TimeSpan DownstreamWriteTimeout, TimeSpan TlsHandshakeTimeout, TimeSpan ClientKeepAliveIdleTimeout, TimeSpan UpstreamIdleConnectionLifetime, TimeSpan TunnelIdleTimeout)
    {
        RuntimeTimeoutFacts.Validate(ClientRequestHeadTimeout, ClientRequestBodyIdleTimeout, UpstreamConnectTimeout, UpstreamResponseHeadTimeout, UpstreamResponseBodyIdleTimeout, DownstreamWriteTimeout, TlsHandshakeTimeout, ClientKeepAliveIdleTimeout, UpstreamIdleConnectionLifetime, TunnelIdleTimeout);
        this.ClientRequestHeadTimeout = ClientRequestHeadTimeout;
        this.ClientRequestBodyIdleTimeout = ClientRequestBodyIdleTimeout;
        this.UpstreamConnectTimeout = UpstreamConnectTimeout;
        this.UpstreamResponseHeadTimeout = UpstreamResponseHeadTimeout;
        this.UpstreamResponseBodyIdleTimeout = UpstreamResponseBodyIdleTimeout;
        this.DownstreamWriteTimeout = DownstreamWriteTimeout;
        this.TlsHandshakeTimeout = TlsHandshakeTimeout;
        this.ClientKeepAliveIdleTimeout = ClientKeepAliveIdleTimeout;
        this.UpstreamIdleConnectionLifetime = UpstreamIdleConnectionLifetime;
        this.TunnelIdleTimeout = TunnelIdleTimeout;
    }

    public TimeSpan ClientRequestHeadTimeout { get; }
    public TimeSpan ClientRequestBodyIdleTimeout { get; }
    public TimeSpan UpstreamConnectTimeout { get; }
    public TimeSpan UpstreamResponseHeadTimeout { get; }
    public TimeSpan UpstreamResponseBodyIdleTimeout { get; }
    public TimeSpan DownstreamWriteTimeout { get; }
    public TimeSpan TlsHandshakeTimeout { get; }
    public TimeSpan ClientKeepAliveIdleTimeout { get; }
    public TimeSpan UpstreamIdleConnectionLifetime { get; }
    public TimeSpan TunnelIdleTimeout { get; }
}
