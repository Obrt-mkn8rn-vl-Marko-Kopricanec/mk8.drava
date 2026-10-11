using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class ApplicationWriterLeaseProcessTests
{
    [Fact]
    public async Task ASecondApplicationCannotReuseLiveStateOrDisruptThePrivateListenerAsync()
    {
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort(), manualRoute: false).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var bootstrap = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(proxy.ApplicationPlanPath)!, "..", "application.json"));
        var second = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.Application"), bootstrap);
        await using var secondLifetime = second.ConfigureAwait(true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.NotEqual(0, await second.WaitForExitAsync(deadline.Token).ConfigureAwait(true));
        await second.DisposeAsync().ConfigureAwait(true);
        Assert.Contains(OperatingSystem.IsLinux() ? "Private writer lease is unavailable" : "application.lock", second.CapturedLog, StringComparison.Ordinal);
        using var response = await proxy.Client.GetAsync(new Uri("/", UriKind.Relative), deadline.Token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RetiringTheApplicationAllowsItsStateAndPrivateListenerToBeReopenedAsync()
    {
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort(), manualRoute: false).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        await VerifyIdleGatewayConnectionAsync(proxy.Port).ConfigureAwait(true);
        await proxy.StopApplicationAsync().ConfigureAwait(true);
        await proxy.StartApplicationAsync().ConfigureAwait(true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            using var next = await proxy.Client.GetAsync(new Uri("/", UriKind.Relative), deadline.Token).ConfigureAwait(true);
            if (next.StatusCode != HttpStatusCode.ServiceUnavailable)
            {
                Assert.Equal(HttpStatusCode.NotFound, next.StatusCode);
                return;
            }
            await Task.Delay(50, deadline.Token).ConfigureAwait(true);
        }
    }

    private static async Task VerifyIdleGatewayConnectionAsync(int port)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token).ConfigureAwait(true);
        var stream = client.GetStream();
        await stream.WriteAsync("GET / HTTP/1.1\r\nHost: app.test\r\n\r\n"u8.ToArray(), deadline.Token).ConfigureAwait(true);
        var firstHead = await ReadResponseHeadAsync(stream, deadline.Token).ConfigureAwait(true);
        Assert.StartsWith("HTTP/1.1 404 ", firstHead, StringComparison.Ordinal);
        Assert.Contains("\r\nContent-Length: 9\r\n", firstHead, StringComparison.OrdinalIgnoreCase);
        var body = new byte[9];
        await stream.ReadExactlyAsync(body, deadline.Token).ConfigureAwait(true);
        Assert.Equal("Not Found"u8.ToArray(), body);

        // HTTP/1 dispatch on this same physical connection cannot handle the next request
        // until GatewayProxy has joined the preceding private exchange, including RPC EOF.
        await stream.WriteAsync("GET /_drava/live HTTP/1.1\r\nHost: app.test\r\n\r\n"u8.ToArray(), deadline.Token).ConfigureAwait(true);
        var liveHead = await ReadResponseHeadAsync(stream, deadline.Token).ConfigureAwait(true);
        Assert.StartsWith("HTTP/1.1 200 ", liveHead, StringComparison.Ordinal);
        Assert.Contains("\r\nContent-Length: 7\r\n", liveHead, StringComparison.OrdinalIgnoreCase);
        var liveBody = new byte[7];
        await stream.ReadExactlyAsync(liveBody, deadline.Token).ConfigureAwait(true);
        Assert.Equal("running"u8.ToArray(), liveBody);
    }

    private static async Task<string> ReadResponseHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new byte[32768];
        var length = 0;
        while (length < bytes.Length)
        {
            await stream.ReadExactlyAsync(bytes.AsMemory(length, 1), cancellationToken).ConfigureAwait(true);
            if (++length >= 4 && bytes.AsSpan(length - 4, 4).SequenceEqual("\r\n\r\n"u8))
                return Encoding.ASCII.GetString(bytes, 0, length);
        }
        throw new InvalidDataException("Gateway response head exceeds its development bound.");
    }
}
