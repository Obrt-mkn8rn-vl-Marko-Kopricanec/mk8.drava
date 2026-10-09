#pragma warning disable CA1416
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;

namespace Mk8.Drava.Application.INF.Proxy.Http3;
public sealed class Http3UpstreamConnectionPool : IDisposable, IAsyncDisposable
{
    private const int DefaultMaxStreamsPerConnection = 8;
    private readonly ProxyMetrics _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SemaphoreSlim> _keyGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Http3UpstreamPooledConnection>> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _operationsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposalReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _disposeTask;
    private int _activeOperations;
    private bool _disposed;
    public Http3UpstreamConnectionPool(ProxyMetrics metrics, TimeProvider timeProvider)
    {
        _metrics = metrics;
        _timeProvider = timeProvider;
    }

    internal async ValueTask<Http3UpstreamConnection> BorrowAsync(RuntimeUpstream upstream, RuntimeTimeouts timeouts, RuntimeConnectionLimits limits, int maxFramePayloadBytes, CancellationToken cancellationToken)
    {
        var endpoint = UpstreamTransportEndpointMapper.FromUpstream(upstream);
        var key = GetKey(endpoint);
        var gate = BeginOperation(key);
        var entered = false;
        try
        {
            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
            await gate.WaitAsync(stopping.Token).ConfigureAwait(false);
            entered = true;
            ThrowIfDisposed();
            return await BorrowUnderGateAsync(key, endpoint, timeouts, limits, maxFramePayloadBytes, stopping.Token).ConfigureAwait(false);
        }
        finally
        {
            if (entered) gate.Release();
            EndOperation();
        }
    }

    private async ValueTask<Http3UpstreamConnection> BorrowUnderGateAsync(string key, UpstreamTransportEndpoint endpoint, RuntimeTimeouts timeouts,
        RuntimeConnectionLimits limits, int maxFramePayloadBytes, CancellationToken cancellationToken)
    {
        await PruneExpiredIdleConnectionsAsync(key, timeouts.UpstreamIdleConnectionLifetime).ConfigureAwait(false);
        if (ReserveExistingConnection(key, timeouts.UpstreamIdleConnectionLifetime) is ExistingConnectionReservation.Reserved reserved)
        {
            _metrics.UpstreamHttp3PoolConnectionReused();
            return await OpenReservedStreamAsync(key, reserved.Connection, timeouts, maxFramePayloadBytes, cancellationToken).ConfigureAwait(false);
        }

        if (ConnectionCount(key) >= MaxConnectionsFor(limits))
        {
            _metrics.UpstreamHttp3StreamLimitRejected();
            throw new Http3UpstreamProtocolException("All upstream HTTP/3 pooled connections are saturated.", Http3UpstreamFailureKind.ConnectFailure);
        }

        Http3UpstreamTransport transport;
        try { transport = await Http3UpstreamConnection.OpenTransportAsync(endpoint, timeouts, _metrics, cancellationToken).ConfigureAwait(false); }
        catch { _metrics.UpstreamHttp3ConnectionFailed(); throw; }
        Http3UpstreamPooledConnection? untransferred = null;
        var transferred = false;
        try
        {
            // Guarded failures dispose this temporary; successful registration transfers ownership to this pool until async shutdown.
#pragma warning disable CA2000
            untransferred = new Http3UpstreamPooledConnection(key, transport, _metrics, _timeProvider, DefaultMaxStreamsPerConnection);
#pragma warning restore CA2000
            var pooled = untransferred;
            AddConnection(key, pooled);
            if (!pooled.TryReserveStream(timeouts.UpstreamIdleConnectionLifetime))
            {
                pooled.MarkUnusable();
                _metrics.UpstreamHttp3StreamLimitRejected();
                throw new Http3UpstreamProtocolException("New upstream HTTP/3 pooled connection could not reserve a stream.", Http3UpstreamFailureKind.ConnectFailure);
            }

            var connection = await OpenReservedStreamAsync(key, pooled, timeouts, maxFramePayloadBytes, cancellationToken).ConfigureAwait(false);
            untransferred = null; // The registered pool retains ownership beyond this individual stream lease.
            transferred = true;
            return connection;
        }
        finally
        {
            if (untransferred is not null)
            {
                RemoveConnection(key, untransferred);
                await untransferred.DisposeAsync().ConfigureAwait(false);
            }
            else if (!transferred)
            {
                try { await transport.ControlStream.DisposeAsync().ConfigureAwait(false); }
                finally
                {
                    await transport.Connection.DisposeAsync().ConfigureAwait(false);
                    _metrics.UpstreamHttp3ConnectionClosed();
                    _metrics.UpstreamHttp3PoolConnectionClosed();
                }
            }
        }
    }

    public async ValueTask PruneIdleConnectionsAsync(UpstreamTransportEndpoint endpoint, TimeSpan idleLifetime)
    {
        var key = GetKey(endpoint);
        var gate = BeginOperation(key);
        var entered = false;
        try
        {
            await gate.WaitAsync(_stopping.Token).ConfigureAwait(false);
            entered = true;
            await PruneExpiredIdleConnectionsAsync(key, idleLifetime).ConfigureAwait(false);
        }
        finally
        {
            if (entered) gate.Release();
            EndOperation();
        }
    }

    public void Dispose()
    {
        // Preserve inherited synchronous host disposal; owned I/O continuations never require its calling thread.
#pragma warning disable VSTHRD002
        DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    public ValueTask DisposeAsync()
    {
        Task disposal;
        lock (_gate)
        {
            if (_disposeTask is null)
            {
                _disposed = true;
                if (_activeOperations == 0) _operationsDrained.TrySetResult();
                _disposeTask = DisposeOwnedAsync();
            }
            disposal = _disposeTask;
        }
        _disposalReady.TrySetResult();
        return new ValueTask(disposal);
    }

    private async Task DisposeOwnedAsync()
    {
        // DisposeAsync publishes the shared task, releases the lock and immediately signals this owned source.
#pragma warning disable VSTHRD003
        await _disposalReady.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        try { await _stopping.CancelAsync().ConfigureAwait(false); }
        finally
        {
            try
            {
                // Only this pool's context-free operations can complete this drain, in their finally blocks.
#pragma warning disable VSTHRD003
                await _operationsDrained.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            finally { await CloseConnectionsAndGatesAsync().ConfigureAwait(false); }
        }
    }

    private async Task CloseConnectionsAndGatesAsync()
    {
        List<Http3UpstreamPooledConnection> connections = [];
        SemaphoreSlim[] gates;
        lock (_gate)
        {
            foreach (var entry in _connections.Values) connections.AddRange(entry);
            _connections.Clear();
            gates = _keyGates.Values.ToArray();
            _keyGates.Clear();
        }
        try
        {
            var closing = new Task[connections.Count];
            for (var index = 0; index < connections.Count; index++) closing[index] = connections[index].DisposeAsync().AsTask();
            await Task.WhenAll(closing).ConfigureAwait(false);
        }
        finally
        {
            foreach (var gate in gates) gate.Dispose();
            _stopping.Dispose();
        }
    }

    public static string GetKey(UpstreamTransportEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return $"{endpoint.PoolKey}|alpn=h3|qpack=static-zero";
    }

    private ExistingConnectionReservation ReserveExistingConnection(string key, TimeSpan idleLifetime)
    {
        List<Http3UpstreamPooledConnection>? connections;
        lock (_gate)
        {
            if (!_connections.TryGetValue(key, out connections))
            {
                return ExistingConnectionReservation.Unavailable;
            }
        }

        for (var index = 0; index < connections.Count; index++)
        {
            var connection = connections[index];
            if (!connection.TryReserveStream(idleLifetime))
            {
                continue;
            }

            return ExistingConnectionReservation.Reserve(connection);
        }

        return ExistingConnectionReservation.Unavailable;
    }

    private abstract record ExistingConnectionReservation
    {
        private ExistingConnectionReservation()
        {
        }

        public static ExistingConnectionReservation Unavailable { get; } = new UnavailableReservation();

        public static Reserved Reserve(Http3UpstreamPooledConnection connection)
        {
            return new Reserved(connection);
        }

        public sealed record Reserved : ExistingConnectionReservation
        {
            public Reserved(Http3UpstreamPooledConnection connection)
            {
                ArgumentNullException.ThrowIfNull(connection);
                Connection = connection;
            }

            public Http3UpstreamPooledConnection Connection { get; }
        }

        private sealed record UnavailableReservation : ExistingConnectionReservation;
    }

    private async ValueTask PruneExpiredIdleConnectionsAsync(string key, TimeSpan idleLifetime)
    {
        List<Http3UpstreamPooledConnection> expired = [];
        lock (_gate)
        {
            if (!_connections.TryGetValue(key, out var connections))
            {
                return;
            }

            for (var index = connections.Count - 1; index >= 0; index--)
            {
                var connection = connections[index];
                if (!connection.ShouldPrune(idleLifetime))
                {
                    continue;
                }

                connections.RemoveAt(index);
                expired.Add(connection);
            }

            if (connections.Count == 0)
            {
                _connections.Remove(key);
            }
        }

        for (var index = 0; index < expired.Count; index++)
        {
            await expired[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask<Http3UpstreamConnection> OpenReservedStreamAsync(string key, Http3UpstreamPooledConnection pooled, RuntimeTimeouts timeouts, int maxFramePayloadBytes, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await Http3UpstreamConnection.OpenStreamAsync(pooled, timeouts, _metrics, maxFramePayloadBytes, cancellationToken).ConfigureAwait(false);
            try { ThrowIfDisposed(); return connection; }
            catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
        }
        catch
        {
            RemoveConnection(key, pooled);
            await pooled.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void RemoveConnection(string key, Http3UpstreamPooledConnection pooled)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(key, out var connections))
            {
                return;
            }

            connections.Remove(pooled);
            if (connections.Count == 0)
            {
                _connections.Remove(key);
            }
        }
    }

    private SemaphoreSlim BeginOperation(string key)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_keyGates.TryGetValue(key, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _keyGates.Add(key, gate);
            }

            _activeOperations++;
            return gate;
        }
    }

    private void EndOperation()
    {
        lock (_gate)
        {
            _activeOperations--;
            if (_disposed && _activeOperations == 0) _operationsDrained.TrySetResult();
        }
    }

    private int ConnectionCount(string key)
    {
        lock (_gate)
        {
            return _connections.TryGetValue(key, out var connections) ? connections.Count : 0;
        }
    }

    private void AddConnection(string key, Http3UpstreamPooledConnection pooled)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_connections.TryGetValue(key, out var connections))
            {
                connections = [];
                _connections.Add(key, connections);
            }

            connections.Add(pooled);
        }
    }

    private static int MaxConnectionsFor(RuntimeConnectionLimits limits)
    {
        return Math.Clamp(limits.MaxIdleUpstreamConnectionsPerUpstream, 1, 64);
    }

    private void ThrowIfDisposed()
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
