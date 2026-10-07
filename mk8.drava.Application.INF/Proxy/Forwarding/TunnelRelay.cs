using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Net.Sockets;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;
public sealed partial class TunnelRelay
{
    private readonly ProxyMetrics _metrics;
    private readonly ILogger<TunnelRelay> _logger;
    private readonly TimeProvider _timeProvider;
    public TunnelRelay(ProxyMetrics metrics, ILogger<TunnelRelay> logger, TimeProvider timeProvider)
    {
        _metrics = metrics;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async ValueTask<TunnelRelayResult> RelayAsync(Stream clientStream, Stream upstreamStream, RuntimeListener listener, RuntimeTimeouts timeouts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upstreamStream);
        ArgumentNullException.ThrowIfNull(timeouts);
        ArgumentNullException.ThrowIfNull(clientStream);
        ArgumentNullException.ThrowIfNull(listener);
        using var tunnelCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = tunnelCancellation.Token;
        var lastActivity = _timeProvider.GetTimestamp();
        var idleTimedOut = false;
        var relayFailed = false;
        var bytesClientToUpstream = 0L;
        var bytesUpstreamToClient = 0L;
        var started = _timeProvider.GetTimestamp();
        var clientToUpstream = RelayDirectionAsync(clientStream, upstreamStream, listener.ForwardingBufferBytes, bytes =>
        {
            Volatile.Write(ref lastActivity, _timeProvider.GetTimestamp());
            Interlocked.Add(ref bytesClientToUpstream, bytes);
            _metrics.AddTunnelBytesClientToUpstream(bytes);
        }, () => relayFailed = true, token).AsTask();
        var upstreamToClient = RelayDirectionAsync(upstreamStream, clientStream, listener.ForwardingBufferBytes, bytes =>
        {
            Volatile.Write(ref lastActivity, _timeProvider.GetTimestamp());
            Interlocked.Add(ref bytesUpstreamToClient, bytes);
            _metrics.AddTunnelBytesUpstreamToClient(bytes);
        }, () => relayFailed = true, token).AsTask();
        var idleMonitor = MonitorIdleAsync(timeouts.TunnelIdleTimeout, () => Volatile.Read(ref lastActivity), _timeProvider, () =>
        {
            idleTimedOut = true;
            tunnelCancellation.Cancel();
        }, token);
        try
        {
            var completed = await Task.WhenAny(clientToUpstream, upstreamToClient, idleMonitor).ConfigureAwait(false);
            if (completed == idleMonitor && idleTimedOut)
            {
                _metrics.TunnelIdleTimedOut();
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
                {
                    LogUpgradedTunnelIdleTimeoutElapsed10026(_logger, null);
                }
            }
        }
        finally
        {
            await tunnelCancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await clientToUpstream.ConfigureAwait(false);
            }
            catch (Exception exception)when (IsExpectedTunnelEnd(exception, cancellationToken))
            {
            }

            try
            {
                await upstreamToClient.ConfigureAwait(false);
            }
            catch (Exception exception)when (IsExpectedTunnelEnd(exception, cancellationToken))
            {
            }

            try
            {
                await idleMonitor.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        var finalBytesClientToUpstream = Interlocked.Read(ref bytesClientToUpstream);
        var finalBytesUpstreamToClient = Interlocked.Read(ref bytesUpstreamToClient);
        var duration = _timeProvider.GetElapsedTime(started);
        if (idleTimedOut)
        {
            return TunnelRelayResult.IdleTimedOut(finalBytesClientToUpstream, finalBytesUpstreamToClient, duration);
        }

        if (relayFailed)
        {
            return TunnelRelayResult.RelayFailed(finalBytesClientToUpstream, finalBytesUpstreamToClient, duration);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return TunnelRelayResult.Shutdown(finalBytesClientToUpstream, finalBytesUpstreamToClient, duration);
        }

        return TunnelRelayResult.Closed(finalBytesClientToUpstream, finalBytesUpstreamToClient, duration);
    }

    private async ValueTask RelayDirectionAsync(Stream source, Stream destination, int bufferSize, Action<int> onBytesRelayed, Action onRelayFailure, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return;
                }

                _metrics.AddBytesRead(bytesRead);
                onBytesRelayed(bytesRead);
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                _metrics.AddBytesWritten(bytesRead);
            }
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)when (exception is IOException or SocketException)
        {
            _metrics.TunnelRelayFailed();
            onRelayFailure();
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogUpgradedTunnelRelayEndedWith10027(_logger, exception);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task MonitorIdleAsync(TimeSpan idleTimeout, Func<long> getLastActivityTimestamp, TimeProvider timeProvider, Action onIdleTimeout, CancellationToken cancellationToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(Math.Min(250, Math.Max(25, idleTimeout.TotalMilliseconds / 4)));
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(pollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
            var elapsed = timeProvider.GetElapsedTime(getLastActivityTimestamp());
            if (elapsed >= idleTimeout)
            {
                onIdleTimeout();
                return;
            }
        }
    }

    private static bool IsExpectedTunnelEnd(Exception exception, CancellationToken outerToken)
    {
        return exception is OperationCanceledException || exception is IOException || exception is SocketException || outerToken.IsCancellationRequested;
    }
}
