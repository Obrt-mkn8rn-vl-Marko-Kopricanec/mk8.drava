namespace Mk8.Drava.Contracts.Administration.V1;

public sealed record RuntimeRouteTimeoutOverridesResponse(TimeSpan? ClientRequestBodyIdleTimeout, TimeSpan? UpstreamConnectTimeout,
    TimeSpan? UpstreamResponseBodyIdleTimeout, TimeSpan? DownstreamWriteTimeout);
