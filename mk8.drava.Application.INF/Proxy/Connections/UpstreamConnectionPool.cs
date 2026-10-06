using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;

namespace Mk8.Drava.Application.INF.Proxy.Connections;
public sealed class UpstreamConnectionPool : IUpstreamConnectionPruner, IDisposable
{
    private readonly UpstreamConnectionFactory _connectionFactory;
    private readonly ProxyMetrics _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Queue<PooledUpstreamConnection>> _idleConnections = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    public UpstreamConnectionPool(UpstreamConnectionFactory connectionFactory, ProxyMetrics metrics, TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _metrics = metrics;
        _timeProvider = timeProvider;
    }

    public async ValueTask<UpstreamConnectionLease> BorrowAsync(RuntimeUpstream upstream, RuntimeTimeouts timeouts, RuntimeConnectionLimits limits, CancellationToken cancellationToken)
    {
        var endpoint = UpstreamTransportEndpointMapper.FromUpstream(upstream);
        var key = GetKey(endpoint);
        var nowUtc = _timeProvider.GetUtcNow();
        PooledUpstreamConnection? connection = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_idleConnections.TryGetValue(key, out var queue))
            {
                while (queue.Count > 0)
                {
                    var candidate = queue.Dequeue();
                    _metrics.UpstreamPoolIdleConnectionDiscarded();
                    if (IsExpired(candidate, timeouts.UpstreamIdleConnectionLifetime, nowUtc))
                    {
                        candidate.Dispose();
                        _metrics.UpstreamConnectionDiscarded();
                        continue;
                    }

                    connection = candidate;
                    break;
                }
            }
        }

        if (connection is not null)
        {
            connection.MarkBorrowed(limits.MaxIdleUpstreamConnectionsPerUpstream);
            _metrics.UpstreamConnectionReused();
            _metrics.UpstreamPoolConnectionBorrowed();
            return new UpstreamConnectionLease(this, connection);
        }

        var transport = await _connectionFactory.ConnectAsync(endpoint, timeouts.UpstreamConnectTimeout, cancellationToken).ConfigureAwait(false);
        connection = new PooledUpstreamConnection(key, endpoint, transport.Socket, transport.Stream, _timeProvider.GetUtcNow());
        connection.MarkBorrowed(limits.MaxIdleUpstreamConnectionsPerUpstream);
        _metrics.UpstreamConnectionOpened();
        _metrics.UpstreamPoolConnectionBorrowed();
        return new UpstreamConnectionLease(this, connection);
    }

    internal void Return(PooledUpstreamConnection connection)
    {
        if (!connection.CanReturnToPool || _disposed)
        {
            connection.Dispose();
            _metrics.UpstreamConnectionDiscarded();
            _metrics.UpstreamPoolConnectionClosedActive();
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                connection.Dispose();
                _metrics.UpstreamConnectionDiscarded();
                _metrics.UpstreamPoolConnectionClosedActive();
                return;
            }

            var queue = GetOrCreateQueue(connection.Key);
            if (queue.Count >= connection.MaxIdleConnections)
            {
                connection.Dispose();
                _metrics.UpstreamConnectionDiscarded();
                _metrics.UpstreamPoolConnectionClosedActive();
                return;
            }

            connection.MarkReturnedIdle(_timeProvider.GetUtcNow());
            queue.Enqueue(connection);
            _metrics.UpstreamPoolConnectionReturnedIdle();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var queue in _idleConnections.Values)
            {
                while (queue.Count > 0)
                {
                    var connection = queue.Dequeue();
                    connection.Dispose();
                    _metrics.UpstreamConnectionDiscarded();
                    _metrics.UpstreamPoolIdleConnectionDiscarded();
                }
            }

            _idleConnections.Clear();
        }
    }

    public void PruneIdleConnections(UpstreamTransportEndpoint endpoint)
    {
        var key = GetKey(endpoint);
        lock (_gate)
        {
            if (!_idleConnections.TryGetValue(key, out var queue))
            {
                return;
            }

            while (queue.Count > 0)
            {
                var connection = queue.Dequeue();
                connection.Dispose();
                _metrics.UpstreamConnectionDiscarded();
                _metrics.UpstreamPoolIdleConnectionDiscarded();
            }

            _idleConnections.Remove(key);
        }
    }

    private Queue<PooledUpstreamConnection> GetOrCreateQueue(string key)
    {
        if (!_idleConnections.TryGetValue(key, out var queue))
        {
            queue = new Queue<PooledUpstreamConnection>();
            _idleConnections.Add(key, queue);
        }

        return queue;
    }

    private static bool IsExpired(PooledUpstreamConnection connection, TimeSpan idleLifetime, DateTimeOffset nowUtc)
    {
        return nowUtc - connection.LastUsedUtc > idleLifetime;
    }

    public static string GetKey(UpstreamTransportEndpoint endpoint)
    {
        return endpoint.PoolKey;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(UpstreamConnectionPool));
        }
    }
}
