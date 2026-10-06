namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeTimeoutFacts
{
    private static readonly TimeSpan MinimumTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(10);
    public static void Validate(TimeSpan clientRequestHeadTimeout, TimeSpan clientRequestBodyIdleTimeout, TimeSpan upstreamConnectTimeout, TimeSpan upstreamResponseHeadTimeout, TimeSpan upstreamResponseBodyIdleTimeout, TimeSpan downstreamWriteTimeout, TimeSpan tlsHandshakeTimeout, TimeSpan clientKeepAliveIdleTimeout, TimeSpan upstreamIdleConnectionLifetime, TimeSpan tunnelIdleTimeout)
    {
        ValidateTimeout(clientRequestHeadTimeout, nameof(clientRequestHeadTimeout));
        ValidateTimeout(clientRequestBodyIdleTimeout, nameof(clientRequestBodyIdleTimeout));
        ValidateTimeout(upstreamConnectTimeout, nameof(upstreamConnectTimeout));
        ValidateTimeout(upstreamResponseHeadTimeout, nameof(upstreamResponseHeadTimeout));
        ValidateTimeout(upstreamResponseBodyIdleTimeout, nameof(upstreamResponseBodyIdleTimeout));
        ValidateTimeout(downstreamWriteTimeout, nameof(downstreamWriteTimeout));
        ValidateTimeout(tlsHandshakeTimeout, nameof(tlsHandshakeTimeout));
        ValidateTimeout(clientKeepAliveIdleTimeout, nameof(clientKeepAliveIdleTimeout));
        ValidateTimeout(upstreamIdleConnectionLifetime, nameof(upstreamIdleConnectionLifetime));
        ValidateTimeout(tunnelIdleTimeout, nameof(tunnelIdleTimeout));
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout < MinimumTimeout || timeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
