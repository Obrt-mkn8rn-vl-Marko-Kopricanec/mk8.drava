using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.Configuration;
using Mk8.Drava.Presentation.Proxy;
using Mk8.Drava.Transport.Clients;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class TransportFailureCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedTlsHandshakeClosesItsConnectedSocketBeforeReturningAsync(bool callerCancellation)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var endpoint = new UpstreamTransportEndpoint("stalled", "https", "http1", "127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, false, "stalled.test");
        var connecting = new UpstreamConnectionFactory().ConnectAsync(endpoint, TimeSpan.FromSeconds(callerCancellation ? 10 : 2), caller.Token).AsTask();
        try
        {
            using var peer = await listener.AcceptTcpClientAsync(lifetime.Token).ConfigureAwait(true);
            var stream = peer.GetStream();
            var recordHeader = new byte[5];
            await stream.ReadExactlyAsync(recordHeader, lifetime.Token).ConfigureAwait(true);
            Assert.Equal(22, recordHeader[0]); // The TCP connection entered an actual TLS handshake.
            if (callerCancellation) await caller.CancelAsync().ConfigureAwait(true);
            Exception? failure = null;
            try { using var unexpected = await connecting.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or ProxyTimeoutException or IOException) { failure = exception; }
            if (callerCancellation) Assert.IsAssignableFrom<OperationCanceledException>(failure);
            else Assert.IsType<ProxyTimeoutException>(failure);
            Assert.True(await ReachesClosedAsync(stream, lifetime.Token).ConfigureAwait(true));
        }
        finally
        {
            await caller.CancelAsync().ConfigureAwait(true);
            try { using var unexpected = await connecting.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or ProxyTimeoutException or IOException) { }
        }
    }

    [Fact]
    public async Task GatewayClassifiesInvalidFramingAndReleasesAdmissionForTheNextRequestAsync()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "drava_invalid_frame_" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var token = Path.Combine(directory, "gateway.token");
            await File.WriteAllTextAsync(token, new string('A', 64)).ConfigureAwait(true);
            var endpoint = OperatingSystem.IsWindows() ? new IpcEndpoint { NamedPipeName = "drava_missing_" + Guid.NewGuid().ToString("N"), IdentityTokenPath = token }
                : new IpcEndpoint { UnixSocketPath = Path.Combine(directory, "missing.sock"), IdentityTokenPath = token };
            using var channel = new ApplicationChannel(endpoint);
            using var gateway = new GatewayProxy(channel, new GatewayBootstrap { Application = endpoint, MaxConcurrentExchanges = 1 });
            for (var index = 0; index < 2; index++)
            {
                var context = InvalidContext();
                await gateway.InvokeAsync(context).ConfigureAwait(true);
                Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static DefaultHttpContext InvalidContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET"; context.Request.Scheme = "http"; context.Request.Protocol = "HTTP/1.1";
        context.Request.Host = new HostString("service.test"); context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/";
        context.Request.Headers["x-invalid"] = "line\rbreak";
        return context;
    }

    private static async Task<bool> ReachesClosedAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            while (await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) != 0) { }
            return true;
        }
        catch (IOException exception) when (exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted })
        { return true; }
    }
}
