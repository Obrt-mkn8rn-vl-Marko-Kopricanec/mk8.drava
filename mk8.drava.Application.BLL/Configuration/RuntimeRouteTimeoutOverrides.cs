namespace Mk8.Drava.Application.BLL.Configuration;

public sealed record RuntimeRouteTimeoutOverrides
{
    public static RuntimeRouteTimeoutOverrides None { get; } = new(ClientRequestBodyIdleTimeout: null,
        UpstreamConnectTimeout: null, UpstreamResponseBodyIdleTimeout: null, DownstreamWriteTimeout: null);

    public RuntimeRouteTimeoutOverrides(TimeSpan? ClientRequestBodyIdleTimeout, TimeSpan? UpstreamConnectTimeout,
        TimeSpan? UpstreamResponseBodyIdleTimeout, TimeSpan? DownstreamWriteTimeout)
    {
        if (ClientRequestBodyIdleTimeout is { } upload)
            RuntimeTimeoutFacts.ValidateFlowTimeout(upload, nameof(ClientRequestBodyIdleTimeout));
        if (UpstreamConnectTimeout is { } connect)
            RuntimeTimeoutFacts.ValidateTimeout(connect, nameof(UpstreamConnectTimeout));
        if (UpstreamResponseBodyIdleTimeout is { } response)
            RuntimeTimeoutFacts.ValidateFlowTimeout(response, nameof(UpstreamResponseBodyIdleTimeout));
        if (DownstreamWriteTimeout is { } write)
            RuntimeTimeoutFacts.ValidateFlowTimeout(write, nameof(DownstreamWriteTimeout));
        this.ClientRequestBodyIdleTimeout = ClientRequestBodyIdleTimeout;
        this.UpstreamConnectTimeout = UpstreamConnectTimeout;
        this.UpstreamResponseBodyIdleTimeout = UpstreamResponseBodyIdleTimeout;
        this.DownstreamWriteTimeout = DownstreamWriteTimeout;
    }

    public TimeSpan? ClientRequestBodyIdleTimeout { get; }
    public TimeSpan? UpstreamConnectTimeout { get; }
    public TimeSpan? UpstreamResponseBodyIdleTimeout { get; }
    public TimeSpan? DownstreamWriteTimeout { get; }
    public bool IsEmpty => ClientRequestBodyIdleTimeout is null && UpstreamConnectTimeout is null &&
        UpstreamResponseBodyIdleTimeout is null && DownstreamWriteTimeout is null;
}
