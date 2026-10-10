using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
public static class ProxyTimeoutPolicy
{
    public static async ValueTask RunAsync(Func<CancellationToken, ValueTask> operation, TimeSpan timeout, ProxyTimeoutKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await operation(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new ProxyTimeoutException(kind, timeout);
        }
    }

    public static async ValueTask<T> RunAsync<T>(Func<CancellationToken, ValueTask<T>> operation, TimeSpan timeout, ProxyTimeoutKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            return await operation(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new ProxyTimeoutException(kind, timeout);
        }
    }

    public static RuntimeTimeouts ApplyRouteTimeouts(ProxyRouteTimeoutPolicyInput input, RuntimeTimeouts timeouts)
    {
        ArgumentNullException.ThrowIfNull(timeouts);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.FlowTimeouts, nameof(input));
        var flow = input.FlowTimeouts;
        return new RuntimeTimeouts(timeouts.ClientRequestHeadTimeout,
            flow.ClientRequestBodyIdleTimeout ?? timeouts.ClientRequestBodyIdleTimeout,
            flow.UpstreamConnectTimeout ?? timeouts.UpstreamConnectTimeout, input.UpstreamResponseHeadTimeout,
            flow.UpstreamResponseBodyIdleTimeout ?? timeouts.UpstreamResponseBodyIdleTimeout,
            flow.DownstreamWriteTimeout ?? timeouts.DownstreamWriteTimeout, timeouts.TlsHandshakeTimeout,
            timeouts.ClientKeepAliveIdleTimeout, timeouts.UpstreamIdleConnectionLifetime, timeouts.TunnelIdleTimeout);
    }

    public static RuntimeTimeouts ApplyRetryAttemptTimeout(ProxyRouteTimeoutPolicyInput input, RuntimeTimeouts timeouts)
    {
        ArgumentNullException.ThrowIfNull(timeouts);
        ArgumentNullException.ThrowIfNull(input);
        if (input.RetryPerAttemptTimeout is not { } perAttemptTimeout)
        {
            return timeouts;
        }

        return WithUpstreamTimeouts(timeouts, perAttemptTimeout, perAttemptTimeout);
    }

    private static RuntimeTimeouts WithUpstreamTimeouts(RuntimeTimeouts source, TimeSpan upstreamConnectTimeout, TimeSpan upstreamResponseHeadTimeout)
    {
        return new RuntimeTimeouts(source.ClientRequestHeadTimeout, source.ClientRequestBodyIdleTimeout, upstreamConnectTimeout, upstreamResponseHeadTimeout, source.UpstreamResponseBodyIdleTimeout, source.DownstreamWriteTimeout, source.TlsHandshakeTimeout, source.ClientKeepAliveIdleTimeout, source.UpstreamIdleConnectionLifetime, source.TunnelIdleTimeout);
    }
}
