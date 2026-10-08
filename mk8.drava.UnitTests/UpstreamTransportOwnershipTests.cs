using System.Net;
using System.Net.Sockets;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class UpstreamTransportOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SocketClosesEvenWhenTheOwnedStreamFailsDisposal(bool streamFailure)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var stream = new DevelopmentDisposalFailureStream(streamFailure);
        using var connection = new UpstreamTransportConnection(new UpstreamTransportEndpoint("peer", "http", "http1", "127.0.0.1", 8080, false, ""), socket, stream);
        if (streamFailure)
        {
            var error = Assert.Throws<IOException>(connection.Dispose);
            Assert.Equal("Development owned stream disposal failure.", error.Message);
        }
        else connection.Dispose();
        Assert.True(socket.SafeHandle.IsClosed);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task CancelingAPartiallyStartedTlsConnectionClosesTheActualPeerSocketAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var endpoint = new UpstreamTransportEndpoint("peer", "https", "http1", "127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, false, "upstream.test");
        var factory = new UpstreamConnectionFactory();
        var pending = factory.ConnectAsync(endpoint, TimeSpan.FromSeconds(10), operation.Token).AsTask();
        try
        {
            using var peer = await listener.AcceptTcpClientAsync(deadline.Token).ConfigureAwait(true);
            var stream = peer.GetStream();
            var bytes = new byte[4096];
            Assert.True(await stream.ReadAsync(bytes, deadline.Token).ConfigureAwait(true) > 0);
            await operation.CancelAsync().ConfigureAwait(true);
            var canceled = false;
            try { using var unexpected = await pending.ConfigureAwait(true); }
            catch (OperationCanceledException) when (operation.IsCancellationRequested) { canceled = true; }
            Assert.True(canceled);
            while (await stream.ReadAsync(bytes, deadline.Token).ConfigureAwait(true) > 0) { }
        }
        finally
        {
            await operation.CancelAsync().ConfigureAwait(true);
            try { using var unexpected = await pending.ConfigureAwait(true); }
            catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        }
    }
}
