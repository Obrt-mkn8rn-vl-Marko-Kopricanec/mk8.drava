using System.Net;
using System.Net.Sockets;
using System.Text;
using Grpc.Core;
using Mk8.Drava.Transport.Clients;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class TwoProcessExchangeTests
{
    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025", Justification = "The finally block cancels and joins the upstream task on success or failure before the using-scoped listener and token source are disposed. The analyzer does not recognize this structured teardown.")]
    public async Task StreamsNativeUpstreamResponseAcrossIndependentProcessesAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var upstream = RespondAsync(listener, "HTTP/1.1 200 OK\r\nContent-Length: 11\r\nConnection: close\r\nX-Upstream: native\r\n\r\nhello drava", timeout.Token);
        try
        {
            var proxy = await TwoProcessProxy.StartAsync(((IPEndPoint)listener.LocalEndpoint).Port).ConfigureAwait(true);
            await using var lifetime = proxy.ConfigureAwait(true);
            using var response = await proxy.Client.GetAsync(new Uri("path?exact=%2F", UriKind.Relative), timeout.Token).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("hello drava", await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(true));
            var head = await upstream.ConfigureAwait(true);
            Assert.StartsWith("GET /path?exact=%2F HTTP/1.1\r\n", head, StringComparison.Ordinal);
            Assert.Contains("Host: app.test\r\n", head, StringComparison.Ordinal);
            Assert.Equal(["native"], response.Headers.GetValues("X-Upstream"), StringComparer.Ordinal);
        }
        finally
        {
            await timeout.CancelAsync().ConfigureAwait(true);
            try { await upstream.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or IOException) { }
        }
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025", Justification = "The finally block cancels and joins the upstream task on success or failure before the using-scoped listener and token source are disposed. The analyzer does not recognize this structured teardown.")]
    public async Task HandlesEarlyResponseWithoutWaitingForRequestBodyAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var upstream = RespondAsync(listener, "HTTP/1.1 413 Payload Too Large\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", timeout.Token);
        try
        {
            var proxy = await TwoProcessProxy.StartAsync(((IPEndPoint)listener.LocalEndpoint).Port).ConfigureAwait(true);
            await using var lifetime = proxy.ConfigureAwait(true);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port, timeout.Token).ConfigureAwait(true);
            var stream = client.GetStream();
            await stream.WriteAsync("POST /large HTTP/1.1\r\nHost: app.test\r\nContent-Length: 900000\r\n\r\n"u8.ToArray(), timeout.Token).ConfigureAwait(true);
            var response = await ReadHeadAsync(stream, timeout.Token).ConfigureAwait(true);
            Assert.StartsWith("HTTP/1.1 413 ", response, StringComparison.Ordinal);
            Assert.StartsWith("POST /large HTTP/1.1", await upstream.ConfigureAwait(true), StringComparison.Ordinal);
        }
        finally
        {
            await timeout.CancelAsync().ConfigureAwait(true);
            try { await upstream.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or IOException) { }
        }
    }

    [Fact]
    public async Task RejectsUnauthenticatedPrivateExchangeAsync()
    {
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort()).ConfigureAwait(true);
        await using var lifetime = proxy.ConfigureAwait(true);
        using var channel = new ApplicationChannel(proxy.Ipc);
        var client = new ProxyExchange.ProxyExchangeClient(channel.Invoker);
        using var call = client.Exchange(cancellationToken: CancellationToken.None);
        await call.RequestStream.WriteAsync(new ExchangeFrame { Request = new RequestHead { Version = FrameLimits.Version } }).ConfigureAwait(true);
        await call.RequestStream.CompleteAsync().ConfigureAwait(true);
        var error = await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None)).ConfigureAwait(true);
        Assert.Equal(StatusCode.Unauthenticated, error.StatusCode);
    }

    private static async Task<string> RespondAsync(TcpListener listener, string response, CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(true);
        var stream = socket.GetStream();
        var head = await ReadHeadAsync(stream, cancellationToken).ConfigureAwait(true);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).ConfigureAwait(true);
        return head;
    }

    private static async Task<string> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new byte[32768];
        var length = 0;
        while (length < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(length, 1), cancellationToken).ConfigureAwait(true);
            if (count == 0) throw new EndOfStreamException("Expected a complete HTTP head.");
            length += count;
            if (length >= 4 && bytes.AsSpan(length - 4, 4).SequenceEqual("\r\n\r\n"u8)) return Encoding.ASCII.GetString(bytes, 0, length);
        }
        throw new InvalidDataException("HTTP head exceeded its bound.");
    }
}
