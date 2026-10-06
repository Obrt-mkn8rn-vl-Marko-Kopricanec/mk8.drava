namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeRouteResolvedOptionsProjection
{
    public RuntimeRouteResolvedOptionsProjection(long MaxRequestBodyBytes, TimeSpan ClientRequestHeadTimeout, TimeSpan UpstreamResponseHeadTimeout, bool AccessLogEnabled)
    {
        RuntimeRouteResolvedFacts.Validate(MaxRequestBodyBytes, ClientRequestHeadTimeout, UpstreamResponseHeadTimeout);
        this.MaxRequestBodyBytes = MaxRequestBodyBytes;
        this.ClientRequestHeadTimeout = ClientRequestHeadTimeout;
        this.UpstreamResponseHeadTimeout = UpstreamResponseHeadTimeout;
        this.AccessLogEnabled = AccessLogEnabled;
    }

    public long MaxRequestBodyBytes { get; }
    public TimeSpan ClientRequestHeadTimeout { get; }
    public TimeSpan UpstreamResponseHeadTimeout { get; }
    public bool AccessLogEnabled { get; }
}
