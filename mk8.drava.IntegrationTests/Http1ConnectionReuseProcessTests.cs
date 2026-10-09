using System.Net;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class Http1ConnectionReuseProcessTests
{
    [Theory]
    [InlineData(false, false, "fixed")]
    [InlineData(false, true, "fixed")]
    [InlineData(true, false, "fixed")]
    [InlineData(true, true, "fixed")]
    [InlineData(false, false, "chunked")]
    [InlineData(true, false, "chunked")]
    [InlineData(false, false, "head")]
    [InlineData(true, false, "head")]
    public async Task ExcessResponseBytesPreventConnectionReuseAsync(bool publicHttp2, bool cacheEnabled, string framing)
    {
        ArgumentNullException.ThrowIfNull(framing);
        var first = framing switch
        {
            "chunked" => "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n2\r\nok\r\n0\r\n\r\n",
            "head" => "HTTP/1.1 200 OK\r\nContent-Length: 42\r\n\r\n",
            _ => "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nCache-Control: max-age=60\r\n\r\nok",
        };
        await VerifyAsync(publicHttp2, cacheEnabled, string.Equals(framing, "head", StringComparison.Ordinal), first + "HTTP/1.1 200 OK\r\nContent-Length: 6\r\n\r\npoison", 2).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACompletedValidResponseKeepsTheBackendConnectionReusableAsync(bool publicHttp2)
    {
        await VerifyAsync(publicHttp2, false, false, "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", 1).ConfigureAwait(true);
    }

    private static async Task VerifyAsync(bool publicHttp2, bool cacheEnabled, bool head, string firstResponse, int expectedConnections)
    {
        using var upstream = new DevelopmentHttp1Peer();
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, manualCache: cacheEnabled).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        client.Client.DefaultRequestVersion = publicHttp2 ? HttpVersion.Version20 : HttpVersion.Version11;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var peer = upstream.RespondTwiceAsync(firstResponse, deadline.Token);
        try
        {
            using var request = new HttpRequestMessage(head ? HttpMethod.Head : HttpMethod.Get, new Uri("/first", UriKind.Relative))
                { Version = client.Client.DefaultRequestVersion, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            using var first = await client.Client.SendAsync(request, deadline.Token).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(request.Version, first.Version);
            Assert.Equal(head ? "" : "ok", await first.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(true));
            using var second = await client.Client.GetAsync(new Uri("/second", UriKind.Relative), deadline.Token).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(request.Version, second.Version);
            Assert.Equal("safe", await second.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(true));
            await peer.ConfigureAwait(true);
            Assert.Equal(expectedConnections, upstream.Connections);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException) { }
        }
    }
}
