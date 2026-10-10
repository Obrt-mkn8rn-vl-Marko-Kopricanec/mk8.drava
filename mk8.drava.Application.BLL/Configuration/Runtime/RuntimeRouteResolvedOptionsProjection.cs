namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeRouteResolvedOptionsProjection
{
    public RuntimeRouteResolvedOptionsProjection(long MaxRequestBodyBytes, TimeSpan ClientRequestHeadTimeout, TimeSpan UpstreamResponseHeadTimeout, bool AccessLogEnabled)
        : this(MaxRequestBodyBytes, ClientRequestHeadTimeout, UpstreamResponseHeadTimeout, AccessLogEnabled, RuntimeRouteTimeoutOverrides.None)
    {
    }

    public RuntimeRouteResolvedOptionsProjection(long MaxRequestBodyBytes, TimeSpan ClientRequestHeadTimeout, TimeSpan UpstreamResponseHeadTimeout,
        bool AccessLogEnabled, RuntimeRouteTimeoutOverrides FlowTimeouts)
    {
        RuntimeRouteResolvedFacts.Validate(MaxRequestBodyBytes, ClientRequestHeadTimeout, UpstreamResponseHeadTimeout);
        ArgumentNullException.ThrowIfNull(FlowTimeouts);
        this.MaxRequestBodyBytes = MaxRequestBodyBytes;
        this.ClientRequestHeadTimeout = ClientRequestHeadTimeout;
        this.UpstreamResponseHeadTimeout = UpstreamResponseHeadTimeout;
        this.AccessLogEnabled = AccessLogEnabled;
        this.FlowTimeouts = FlowTimeouts;
    }

    public long MaxRequestBodyBytes { get; }
    public TimeSpan ClientRequestHeadTimeout { get; }
    public TimeSpan UpstreamResponseHeadTimeout { get; }
    public bool AccessLogEnabled { get; }
    public RuntimeRouteTimeoutOverrides FlowTimeouts { get; }
}
