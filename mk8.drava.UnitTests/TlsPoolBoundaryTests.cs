using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class TlsPoolBoundaryTests
{
    [Theory]
#pragma warning disable CA5398 // These development conformance cases must exercise each named TLS protocol; production uses SslProtocols.None.
    [InlineData(SslProtocols.Tls12, false, false, 0)]
    [InlineData(SslProtocols.Tls13, false, false, 0)]
    [InlineData(SslProtocols.Tls12, true, false, 0)]
    [InlineData(SslProtocols.Tls13, true, false, 0)]
    [InlineData(SslProtocols.Tls12, true, true, 6)]
    [InlineData(SslProtocols.Tls13, true, true, 6)]
    [InlineData(SslProtocols.Tls12, true, true, 1)]
    [InlineData(SslProtocols.Tls13, true, true, 1)]
    [InlineData(SslProtocols.Tls12, true, true, 3)]
    [InlineData(SslProtocols.Tls13, true, true, 3)]
    [InlineData(SslProtocols.Tls12, true, true, 5)]
    [InlineData(SslProtocols.Tls13, true, true, 5)]
#pragma warning restore CA5398
    public async Task DecryptedIdleInputIsDiscardedAndCleanTlsConnectionsRemainReusableAsync(SslProtocols protocol, bool unread, bool fragmented, int prefixBytes)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=upstream.test", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await VerifyBorrowAsync(listener, certificate, protocol, unread, fragmented, prefixBytes, deadline).ConfigureAwait(true);
    }

    private static async Task VerifyBorrowAsync(TcpListener listener, X509Certificate2 certificate, SslProtocols protocol, bool unread, bool fragmented, int prefixBytes, CancellationTokenSource deadline)
    {
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var metrics = new ProxyMetrics();
        using var pool = new UpstreamConnectionPool(new UpstreamConnectionFactory(), metrics, TimeProvider.System);
        var upstream = new RuntimeUpstream("route", "peer", "https", "http1", "127.0.0.1", port, 1, new RuntimeUpstreamTlsOptions(false, "upstream.test"));
        var timeouts = Timeouts();
        var limits = new RuntimeConnectionLimits(10, 1, 1);
        var server = ServeAsync(listener, certificate, protocol, unread, fragmented, prefixBytes, deadline.Token);
        try
        {
            var first = await pool.BorrowAsync(upstream, timeouts, limits, deadline.Token).ConfigureAwait(true);
            await using var firstLifetime = first.ConfigureAwait(true);
            Assert.Equal(protocol, Assert.IsType<SslStream>(first.Stream).SslProtocol);
            var one = new byte[1];
            Assert.Equal(1, await first.Stream.ReadAsync(one, deadline.Token).ConfigureAwait(true));
            Assert.Equal((byte)1, one[0]);
            var socket = Assert.IsType<Socket>(first.Connection.Socket);
            Assert.False(socket.Poll(0, SelectMode.SelectRead));
            first.MarkReusable();
            await first.DisposeAsync().ConfigureAwait(true);
            var next = await pool.BorrowAsync(upstream, timeouts, limits, deadline.Token).ConfigureAwait(true);
            await using var nextLifetime = next.ConfigureAwait(true);
            if (unread) Assert.NotSame(first.Connection, next.Connection);
            else Assert.Same(first.Connection, next.Connection);
            Assert.Equal(unread ? 2L : 1L, metrics.Snapshot().UpstreamPool.ConnectionsOpened);
            await next.Stream.WriteAsync(new byte[] { 7 }, deadline.Token).ConfigureAwait(true);
            Assert.Equal(1, await next.Stream.ReadAsync(one, deadline.Token).ConfigureAwait(true));
            Assert.Equal((byte)2, one[0]);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await server.ConfigureAwait(true); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
    }

    private static async Task ServeAsync(TcpListener listener, X509Certificate2 certificate, SslProtocols protocol, bool unread, bool fragmented, int prefixBytes, CancellationToken cancellationToken)
    {
        using var first = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        using var fragments = new FragmentedTlsRecordStream(first.GetStream(), prefixBytes);
        var tls = new SslStream(fragments, leaveInnerStreamOpen: true);
        await using var tlsLifetime = tls.ConfigureAwait(false);
        var options = new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = protocol, AllowTlsResume = false };
        await tls.AuthenticateAsServerAsync(options, cancellationToken).ConfigureAwait(false);
        if (fragmented) fragments.Arm();
        var payload = new byte[unread && !fragmented ? 4096 : 1];
        Array.Fill(payload, (byte)1);
        await tls.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        if (fragmented) await tls.WriteAsync(new byte[] { 9 }, cancellationToken).ConfigureAwait(false);
        await tls.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (!unread)
        {
            await ReadAndRespondAsync(tls, cancellationToken).ConfigureAwait(false);
            return;
        }
        using var second = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        var secondTls = new SslStream(second.GetStream(), leaveInnerStreamOpen: true);
        await using var secondLifetime = secondTls.ConfigureAwait(false);
        await secondTls.AuthenticateAsServerAsync(options, cancellationToken).ConfigureAwait(false);
        await ReadAndRespondAsync(secondTls, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadAndRespondAsync(SslStream stream, CancellationToken cancellationToken)
    {
        var one = new byte[1];
        if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) != 1 || one[0] != 7)
            throw new InvalidDataException("Development TLS peer received an unexpected request marker.");
        await stream.WriteAsync(new byte[] { 2 }, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static RuntimeTimeouts Timeouts()
    {
        var value = TimeSpan.FromSeconds(5);
        return new RuntimeTimeouts(value, value, value, value, value, value, value, value, value, value);
    }
}
