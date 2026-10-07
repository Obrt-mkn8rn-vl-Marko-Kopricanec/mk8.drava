using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class Http1UpgradeProcessTests
{
    [Fact]
    public async Task OpaqueUpgradePreservesInitialBytesAndDuplexFramesAcrossProcessesAsync()
    {
        byte[] prefix = [0, 255, 13, 10, 128];
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            var feature = context.Features.Get<IHttpUpgradeFeature>() ?? throw new InvalidOperationException("Development peer has no upgrade feature.");
            context.Response.Headers.Upgrade = "mk8-echo";
            var raw = await feature.UpgradeAsync().ConfigureAwait(false);
            await using var rawLifetime = raw.ConfigureAwait(false);
            await raw.WriteAsync(prefix, context.RequestAborted).ConfigureAwait(false);
            await raw.FlushAsync(context.RequestAborted).ConfigureAwait(false);
            var buffer = new byte[8192];
            int count;
            while ((count = await raw.ReadAsync(buffer, context.RequestAborted).ConfigureAwait(false)) != 0)
            {
                await raw.WriteAsync(buffer.AsMemory(0, count), context.RequestAborted).ConfigureAwait(false);
                await raw.FlushAsync(context.RequestAborted).ConfigureAwait(false);
            }
        }).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port, timeout.Token).ConfigureAwait(true);
        var stream = client.GetStream();
        await stream.WriteAsync("GET /opaque HTTP/1.1\r\nHost: app.test\r\nConnection: Upgrade\r\nUpgrade: mk8-echo\r\n\r\n"u8.ToArray(), timeout.Token).ConfigureAwait(true);
        var head = await ReadHeadAsync(stream, timeout.Token).ConfigureAwait(true);
        Assert.StartsWith("HTTP/1.1 101 ", head, StringComparison.Ordinal);
        Assert.Contains("Upgrade: mk8-echo\r\n", head, StringComparison.OrdinalIgnoreCase);
        var receivedPrefix = new byte[prefix.Length];
        await stream.ReadExactlyAsync(receivedPrefix, timeout.Token).ConfigureAwait(true);
        Assert.Equal(prefix, receivedPrefix);
        var payload = RandomNumberGenerator.GetBytes(256 * 1024);
        var received = new byte[payload.Length];
        await Task.WhenAll(stream.WriteAsync(payload, timeout.Token).AsTask(), stream.ReadExactlyAsync(received, timeout.Token).AsTask()).ConfigureAwait(true);
        Assert.Equal(payload, received);
    }

    [Fact]
    public async Task RejectedUpgradeReturnsOrdinaryHttpResponseAcrossProcessesAsync()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("denied", context.RequestAborted).ConfigureAwait(false);
        }).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("opaque", UriKind.Relative));
        request.Headers.Connection.Add("Upgrade");
        request.Headers.TryAddWithoutValidation("Upgrade", "mk8-echo");
        using var response = await proxy.Client.SendAsync(request, timeout.Token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("denied", await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(true));
    }

    [Fact]
    public async Task WebSocketFragmentsAndCloseSurviveIndependentProcessesAsync()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                await context.Response.WriteAsync("ordinary", context.RequestAborted).ConfigureAwait(false);
                return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync("mk8-echo").ConfigureAwait(false);
            var buffer = new byte[8192];
            while (true)
            {
                var received = await socket.ReceiveAsync(buffer.AsMemory(), context.RequestAborted).ConfigureAwait(false);
                if (received.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "echo-close", context.RequestAborted).ConfigureAwait(false);
                    return;
                }
                await socket.SendAsync(buffer.AsMemory(0, received.Count), received.MessageType, received.EndOfMessage, context.RequestAborted).ConfigureAwait(false);
            }
        }, websockets: true).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var client = new System.Net.WebSockets.ClientWebSocket();
        client.Options.HttpVersion = HttpVersion.Version11;
        client.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        client.Options.SetRequestHeader("Host", "app.test");
        client.Options.AddSubProtocol("mk8-echo");
        await client.ConnectAsync(new UriBuilder("ws", "127.0.0.1", proxy.Port, "socket").Uri, timeout.Token).ConfigureAwait(true);
        Assert.Equal("mk8-echo", client.SubProtocol);
        var payload = RandomNumberGenerator.GetBytes(256 * 1024);
        // Both async helpers return tasks; WhenAll below joins both before socket/token disposal, including failure.
#pragma warning disable CA2025
        var receive = ReceiveWebSocketMessageAsync(client, payload.Length, timeout.Token);
        var send = SendWebSocketFragmentsAsync(client, payload, timeout.Token);
#pragma warning restore CA2025
        await Task.WhenAll(send, receive).ConfigureAwait(true);
        Assert.Equal(payload, await receive.ConfigureAwait(true));
        await client.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "finished", timeout.Token).ConfigureAwait(true);
        Assert.Equal(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, client.CloseStatus);
        using var ordinary = await proxy.Client.GetAsync(new Uri("after-upgrade", UriKind.Relative), timeout.Token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, ordinary.StatusCode);
        Assert.Equal("ordinary", await ordinary.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(true));
    }

    private static async Task SendWebSocketFragmentsAsync(System.Net.WebSockets.WebSocket socket, byte[] payload, CancellationToken cancellationToken)
    {
        await socket.SendAsync(payload.AsMemory(0, 100_000), System.Net.WebSockets.WebSocketMessageType.Binary, endOfMessage: false, cancellationToken).ConfigureAwait(true);
        await socket.SendAsync(payload.AsMemory(100_000), System.Net.WebSockets.WebSocketMessageType.Binary, endOfMessage: true, cancellationToken).ConfigureAwait(true);
    }

    private static async Task<byte[]> ReceiveWebSocketMessageAsync(System.Net.WebSockets.WebSocket socket, int expectedBytes, CancellationToken cancellationToken)
    {
        var bytes = new byte[expectedBytes];
        var offset = 0;
        while (true)
        {
            var received = await socket.ReceiveAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(true);
            Assert.Equal(System.Net.WebSockets.WebSocketMessageType.Binary, received.MessageType);
            offset = checked(offset + received.Count);
            if (received.EndOfMessage) break;
            if (offset == bytes.Length) throw new InvalidDataException("Development WebSocket message exceeds its expected bound.");
        }
        Assert.Equal(expectedBytes, offset);
        return bytes;
    }

    private static async Task<string> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new byte[32768];
        var length = 0;
        while (length < bytes.Length)
        {
            await stream.ReadExactlyAsync(bytes.AsMemory(length, 1), cancellationToken).ConfigureAwait(true);
            if (++length >= 4 && bytes.AsSpan(length - 4, 4).SequenceEqual("\r\n\r\n"u8))
                return Encoding.ASCII.GetString(bytes, 0, length);
        }
        throw new InvalidDataException("Upgrade peer response head exceeds its development bound.");
    }
}
