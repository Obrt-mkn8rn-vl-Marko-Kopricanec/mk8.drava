using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
#pragma warning disable CA1416
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using System.Globalization;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.INF.Proxy.Forwarding;

namespace Mk8.Drava.Application.INF.Proxy.Http3;
internal sealed class Http3UpstreamPooledConnection : IAsyncDisposable
{
    private const int MaxControlFramePayloadBytes = 64 * 1024;
    private readonly Lock _gate = new();
    private readonly ProxyMetrics _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _controlMonitorStop = new();
    private readonly Task _controlMonitor;
    private readonly Func<QuicStream, CancellationToken, Task> _processPeerStream;
    private Task? _disposeTask;
    private Http3UpstreamPooledConnectionState _state = Http3UpstreamPooledConnectionState.Active;
    private int _activeStreams;
    public Http3UpstreamPooledConnection(string key, Http3UpstreamTransport transport, ProxyMetrics metrics, TimeProvider timeProvider, int maxConcurrentStreams)
        : this(key, transport, metrics, timeProvider, maxConcurrentStreams, processPeerStream: null)
    {
    }

    internal Http3UpstreamPooledConnection(string key, Http3UpstreamTransport transport, ProxyMetrics metrics, TimeProvider timeProvider, int maxConcurrentStreams, Func<QuicStream, CancellationToken, Task>? processPeerStream)
    {
        Key = key;
        Connection = transport.Connection;
        ControlStream = transport.ControlStream;
        _metrics = metrics;
        _timeProvider = timeProvider;
        MaxConcurrentStreams = Math.Clamp(maxConcurrentStreams, 1, 64);
        LastUsedUtc = _timeProvider.GetUtcNow();
        _processPeerStream = processPeerStream ?? ProcessInboundStreamAsync;
        _controlMonitor = MonitorPeerStreamsAsync();
    }

    public string Key { get; }
    public QuicConnection Connection { get; }
    private QuicStream ControlStream { get; }
    public int MaxConcurrentStreams { get; }
    public DateTimeOffset LastUsedUtc { get; private set; }

    public Http3UpstreamPooledConnectionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool TryReserveStream(TimeSpan idleLifetime)
    {
        lock (_gate)
        {
            if (_state != Http3UpstreamPooledConnectionState.Active || _activeStreams >= MaxConcurrentStreams)
            {
                return false;
            }

            if (_activeStreams == 0 && _timeProvider.GetUtcNow() - LastUsedUtc > idleLifetime)
            {
                _state = Http3UpstreamPooledConnectionState.IdleExpired;
                return false;
            }

            _activeStreams++;
            return true;
        }
    }

    public bool ShouldPrune(TimeSpan idleLifetime)
    {
        lock (_gate)
        {
            if (_state is Http3UpstreamPooledConnectionState.Closed or Http3UpstreamPooledConnectionState.ShutdownDisposing)
            {
                return false;
            }

            if (_activeStreams > 0)
            {
                return false;
            }

            if (_state is Http3UpstreamPooledConnectionState.Draining or Http3UpstreamPooledConnectionState.Failed or Http3UpstreamPooledConnectionState.IdleExpired)
            {
                return true;
            }

            if (_timeProvider.GetUtcNow() - LastUsedUtc > idleLifetime)
            {
                _state = Http3UpstreamPooledConnectionState.IdleExpired;
                return true;
            }

            return false;
        }
    }

    public void ReleaseStream(bool connectionUsable)
    {
        lock (_gate)
        {
            if (_activeStreams > 0)
            {
                _activeStreams--;
            }

            if (!connectionUsable && _state is not Http3UpstreamPooledConnectionState.ShutdownDisposing and not Http3UpstreamPooledConnectionState.Closed)
            {
                _state = Http3UpstreamPooledConnectionState.Failed;
            }

            if (_activeStreams == 0)
            {
                LastUsedUtc = _timeProvider.GetUtcNow();
            }
        }
    }

    public void MarkUnusable()
    {
        lock (_gate)
        {
            if (_state is not Http3UpstreamPooledConnectionState.Closed and not Http3UpstreamPooledConnectionState.ShutdownDisposing)
            {
                _state = Http3UpstreamPooledConnectionState.Failed;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposeTask ??= DisposeOwnedAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeOwnedAsync()
    {
        _state = Http3UpstreamPooledConnectionState.ShutdownDisposing;
        try
        {
            try
            {
                await _controlMonitorStop.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                // This owner started the monitor; its production I/O awaits never require a caller's main thread.
#pragma warning disable VSTHRD003
                await _controlMonitor.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or QuicException or IOException or ObjectDisposedException)
        {
        }
        finally
        {
            await CloseTransportAsync().ConfigureAwait(false);
        }
    }

    private async Task CloseTransportAsync()
    {
        try
        {
            await ControlStream.DisposeAsync().ConfigureAwait(false);
            await Connection.CloseAsync(0, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is QuicException or IOException or ObjectDisposedException)
        {
        }
        finally
        {
            try
            {
                await Connection.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _controlMonitorStop.Dispose();
                _metrics.UpstreamHttp3ConnectionClosed();
                _metrics.UpstreamHttp3PoolConnectionClosed();
                lock (_gate)
                {
                    _state = Http3UpstreamPooledConnectionState.Closed;
                }
            }
        }
    }

    private async Task MonitorPeerStreamsAsync()
    {
        var cancellationToken = _controlMonitorStop.Token;
        List<Task> children = [];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                children.RemoveAll(static task => task.IsCompletedSuccessfully);
                if (children.Any(static task => task.IsFaulted || task.IsCanceled))
                {
                    await Task.WhenAll(children.Where(static task => task.IsCompleted)).ConfigureAwait(false);
                }
                var stream = await Connection.AcceptInboundStreamAsync(cancellationToken).ConfigureAwait(false);
                children.Add(_processPeerStream(stream, cancellationToken));
            }
        }
        catch (Exception exception)when (exception is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception exception)when (exception is QuicException or IOException)
        {
            MarkUnusableUnlessDisposing();
        }
        finally
        {
            try
            {
                await _controlMonitorStop.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                await Task.WhenAll(children).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessInboundStreamAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        await using var ownedStream = stream.ConfigureAwait(false);
        try
        {
            if (stream.Type != QuicStreamType.Unidirectional)
            {
                await DrainControlStreamAsync(stream, cancellationToken).ConfigureAwait(false);
                return;
            }

            var streamType = await ReadControlVarIntAsync(stream, allowEnd: true, cancellationToken).ConfigureAwait(false);
            if (!streamType.Success)
            {
                return;
            }

            if (streamType.Value != Http3Codec.ControlStream)
            {
                await DrainControlStreamAsync(stream, cancellationToken).ConfigureAwait(false);
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var frameType = await ReadControlVarIntAsync(stream, allowEnd: true, cancellationToken).ConfigureAwait(false);
                if (!frameType.Success)
                {
                    return;
                }

                var length = await ReadControlVarIntAsync(stream, allowEnd: false, cancellationToken).ConfigureAwait(false);
                if (!length.Success || length.Value < 0 || length.Value > MaxControlFramePayloadBytes)
                {
                    MarkUnusable();
                    _metrics.UpstreamHttp3ProtocolError("peer_control_malformed");
                    return;
                }

                var payload = length.Value == 0 ? [] : await ReadExactControlAsync(stream, (int)length.Value, cancellationToken).ConfigureAwait(false);
                if (frameType.Value == Http3Codec.GoAwayFrame)
                {
                    HandleGoAway(payload);
                    continue;
                }

                if (frameType.Value == Http3Codec.SettingsFrame)
                {
                    continue;
                }
            }
        }
        catch (Exception exception)when (exception is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception exception)when (exception is QuicException or IOException)
        {
            MarkUnusableUnlessDisposing();
        }
    }

    private void HandleGoAway(byte[] payload)
    {
        var offset = 0;
        if (payload.Length > 0)
        {
            if (!Http3Codec.TryReadVarInt(payload, ref offset, out var decoded) || offset != payload.Length)
            {
                MarkUnusable();
                _metrics.UpstreamHttp3ProtocolError("goaway_malformed");
                return;
            }
        }

        lock (_gate)
        {
            if (_state == Http3UpstreamPooledConnectionState.Active)
            {
                _state = Http3UpstreamPooledConnectionState.Draining;
            }
        }
    }

    private void MarkUnusableUnlessDisposing()
    {
        lock (_gate)
        {
            if (_state is Http3UpstreamPooledConnectionState.ShutdownDisposing or Http3UpstreamPooledConnectionState.Closed)
            {
                return;
            }

            _state = Http3UpstreamPooledConnectionState.Failed;
        }
    }

    private static async ValueTask DrainControlStreamAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[512];
        while (await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0)
        {
        }
    }

    private static async ValueTask<Http3ControlVarIntReadResult> ReadControlVarIntAsync(QuicStream stream, bool allowEnd, CancellationToken cancellationToken)
    {
        var first = await ReadExactControlAsync(stream, 1, cancellationToken, allowEnd).ConfigureAwait(false);
        if (first.Length == 0)
        {
            return Http3ControlVarIntReadResult.Failure;
        }

        var length = 1 << (first[0] >> 6);
        var value = first[0] & 0x3f;
        if (length == 1)
        {
            return new Http3ControlVarIntReadResult(true, value);
        }

        var rest = await ReadExactControlAsync(stream, length - 1, cancellationToken, allowEnd: false).ConfigureAwait(false);
        foreach (var next in rest)
        {
            value = (value << 8) | next;
        }

        return new Http3ControlVarIntReadResult(true, value);
    }

    private static async ValueTask<byte[]> ReadExactControlAsync(QuicStream stream, int length, CancellationToken cancellationToken, bool allowEnd = false)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return allowEnd && offset == 0 ? [] : throw new IOException("Peer HTTP/3 control stream closed mid frame.");
            }

            offset += read;
        }

        return buffer;
    }

    private readonly record struct Http3ControlVarIntReadResult(bool Success, long Value)
    {
        public static Http3ControlVarIntReadResult Failure { get; } = new(false, 0);
    }
}
