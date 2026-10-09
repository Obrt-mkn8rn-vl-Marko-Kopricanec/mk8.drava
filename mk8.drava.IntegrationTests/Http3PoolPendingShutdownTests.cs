using System.Net.Quic;
using System.Runtime.Versioning;
using System.Security.Authentication;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
public sealed class Http3PoolPendingShutdownTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownRejectsTheHeldConnectionAndSameKeyWaiterAsync(bool queuedBorrower)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var peer = await DevelopmentPendingQuicPeer.CreateAsync(deadline.Token).ConfigureAwait(true);
        await using var peerLifetime = peer.ConfigureAwait(true);
        var metrics = new ProxyMetrics();
        var pool = new Http3UpstreamConnectionPool(metrics, TimeProvider.System);
        await using var poolLifetime = pool.ConfigureAwait(true);
        var upstream = new RuntimeUpstream("route", "peer", "https", "http3", "127.0.0.1", peer.Port, 1, new RuntimeUpstreamTlsOptions(false, "upstream.test"));
        var accepting = peer.AcceptAsync(deadline.Token).AsTask();
        // Shutdown joins admitted borrowers before disposing their gates or connections; finally also observes each task.
#pragma warning disable CA2025
        var borrowing = BorrowAndReleaseAsync(pool, upstream, deadline.Token);
#pragma warning restore CA2025
        Task<bool>? queued = null;
        try
        {
            await peer.HandshakeStarted.WaitAsync(deadline.Token).ConfigureAwait(true);
            if (queuedBorrower)
            {
#pragma warning disable CA2025
                queued = BorrowAndReleaseAsync(pool, upstream, deadline.Token);
#pragma warning restore CA2025
            }
            var stopping = pool.DisposeAsync().AsTask();
            Assert.Same(stopping, pool.DisposeAsync().AsTask());
            peer.ReleaseHandshake();
            await stopping.WaitAsync(deadline.Token).ConfigureAwait(true);
            Assert.True(await borrowing.ConfigureAwait(true), "Pool shutdown allowed its pending handshake to add a connection afterward.");
            if (queued is not null) Assert.True(await queued.ConfigureAwait(true));
            Assert.False(deadline.IsCancellationRequested);
            Assert.Equal(0, metrics.Snapshot().UpstreamHttp3.ActiveConnections);
            Assert.Equal(0, metrics.Snapshot().UpstreamHttp3.ActiveStreams);
        }
        finally
        {
            peer.ReleaseHandshake();
            try { await borrowing.ConfigureAwait(true); if (queued is not null) await queued.ConfigureAwait(true); }
            finally
            {
                try { await pool.PruneIdleConnectionsAsync(UpstreamTransportEndpointMapper.FromUpstream(upstream), TimeSpan.Zero).ConfigureAwait(true); }
                catch (ObjectDisposedException) { }
                finally
                {
                    await peer.StopListenerAsync().ConfigureAwait(true);
                    try { var accepted = await accepting.ConfigureAwait(true); await accepted.DisposeAsync().ConfigureAwait(true); }
                    catch (Exception exception) when (exception is QuicException or OperationCanceledException or ObjectDisposedException or AuthenticationException) { }
                }
            }
        }
    }

    private static async Task<bool> BorrowAndReleaseAsync(Http3UpstreamConnectionPool pool, RuntimeUpstream upstream, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(20);
        var timeouts = new RuntimeTimeouts(timeout, timeout, timeout, timeout, timeout, timeout, timeout, timeout, timeout, timeout);
        try
        {
            var connection = await pool.BorrowAsync(upstream, timeouts, new RuntimeConnectionLimits(8, 1, 1), 64 * 1024, cancellationToken).ConfigureAwait(false);
            await using var owned = connection.ConfigureAwait(false);
            return false;
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException) { return true; }
    }
}
