using System.Net;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class TwoProcessStreamingTests
{
    [Fact]
    public async Task BodyLargerThanBothCreditWindowsStreamsInBothDirectionsAsync()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(static context => context.Request.Body.CopyToAsync(context.Response.Body, context.RequestAborted)).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var body = new byte[1024 * 1024];
        for (var index = 0; index < body.Length; index++) body[index] = (byte)(index % 251);
        using var content = new ByteArrayContent(body);
        using var response = await proxy.Client.PostAsync(new Uri("echo", UriKind.Relative), content).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task RouteRejectionDoesNotSendContinueOrReadTheUploadAsync()
    {
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort()).ConfigureAwait(true);
        await using var lifetime = proxy.ConfigureAwait(true);
        using var socket = new System.Net.Sockets.TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await socket.ConnectAsync(IPAddress.Loopback, proxy.Port, timeout.Token).ConfigureAwait(true);
        var stream = socket.GetStream();
        await stream.WriteAsync("POST / HTTP/1.1\r\nHost: unknown.test\r\nContent-Length: 10\r\nExpect: 100-continue\r\n\r\n"u8.ToArray(), timeout.Token).ConfigureAwait(true);
        var head = new byte[512];
        var length = await stream.ReadAsync(head, timeout.Token).ConfigureAwait(true);
        Assert.StartsWith("HTTP/1.1 404 ", System.Text.Encoding.ASCII.GetString(head, 0, length), StringComparison.Ordinal);
        Assert.DoesNotContain("100 Continue", System.Text.Encoding.ASCII.GetString(head, 0, length), StringComparison.Ordinal);
    }
}
