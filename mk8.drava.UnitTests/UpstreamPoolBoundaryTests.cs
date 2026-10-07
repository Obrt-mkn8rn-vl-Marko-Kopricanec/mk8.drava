using System.Net;
using System.Net.Sockets;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class UpstreamPoolBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObservableIdleInputOrClosureIsDiscardedBeforeAnotherRequestAsync(bool closed)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var metrics = new ProxyMetrics();
        using var pool = new UpstreamConnectionPool(new UpstreamConnectionFactory(), metrics, TimeProvider.System);
        var upstream = new RuntimeUpstream("route", "peer", "http", "http1", "127.0.0.1", port, 1, RuntimeUpstreamTlsOptions.Default);
        var timeouts = Timeouts();
        var limits = new RuntimeConnectionLimits(10, 1, 1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = await pool.BorrowAsync(upstream, timeouts, limits, deadline.Token).ConfigureAwait(true);
        await using var firstLifetime = first.ConfigureAwait(true);
        using var peer = await listener.AcceptTcpClientAsync(deadline.Token).ConfigureAwait(true);
        first.MarkReusable();
        await first.DisposeAsync().ConfigureAwait(true);
        if (closed) peer.Client.Shutdown(SocketShutdown.Send);
        else await peer.GetStream().WriteAsync(new byte[] { 1 }, deadline.Token).ConfigureAwait(true);
        var socket = Assert.IsType<Socket>(first.Connection.Socket);
        while (!socket.Poll(0, SelectMode.SelectRead))
            await Task.Delay(TimeSpan.FromMilliseconds(10), deadline.Token).ConfigureAwait(true);
        var next = await pool.BorrowAsync(upstream, timeouts, limits, deadline.Token).ConfigureAwait(true);
        await using var nextLifetime = next.ConfigureAwait(true);
        Assert.NotSame(first.Connection, next.Connection);
        using var nextPeer = await listener.AcceptTcpClientAsync(deadline.Token).ConfigureAwait(true);
        Assert.Equal(2L, metrics.Snapshot().UpstreamPool.ConnectionsOpened);
        Assert.Equal(0L, metrics.Snapshot().UpstreamPool.ConnectionsReused);
    }

    private static RuntimeTimeouts Timeouts()
    {
        var value = TimeSpan.FromSeconds(5);
        return new RuntimeTimeouts(value, value, value, value, value, value, value, value, value, value);
    }
}
